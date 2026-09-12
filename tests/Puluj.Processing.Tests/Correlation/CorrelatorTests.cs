using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Correlation;
using Puluj.Processing.Indexes;

namespace Puluj.Processing.Tests.Correlation;

public class CorrelatorTests
{
    private static readonly ClassProfile Shahed = new(1, "STRIKE_UAV", 150, 200, true, "uav", 20, 60);
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    // Sumy (34.8, 50.9) -> Poltava (34.55, 49.59) is ~150 km south, ~50 min at 180 km/h.
    private static Observation Obs(double lon, double lat, int minutes, int? model = null, double? dir = null, int? place = null) => new()
    {
        ObservedAt = T0.AddMinutes(minutes),
        ThreatCategoryId = 1,
        ThreatClassId = 1,
        ThreatFamilyId = 1,
        ThreatModelId = model,
        Location = Geo.Point(lon, lat),
        LocationAccuracyKm = 40,
        LocationKind = LocationKind.Region,
        LocationPlaceId = place,
        DirectionDeg = dir,
        DirectionKind = dir is null ? DirectionKind.Unknown : DirectionKind.Compass,
        DirectionConfidence = dir is null ? ConfidenceLevel.Unknown : ConfidenceLevel.High,
        ObservationConfidence = ConfidenceLevel.High,
        EventType = EventType.ThreatObserved,
    };

    [Fact]
    public void Plausible_continuation_scores_above_threshold()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.8, 50.9, 0, dir: 180), T0);
        var next = Obs(34.55, 49.59, 50, dir: 180);
        var score = Correlator.Score(next, track, Shahed, 30);
        Assert.True(score.Total >= 0.6, $"score {score.Total:F2}: {score}");
    }

    [Fact]
    public void Impossible_jump_never_attaches()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.8, 50.9, 0), T0);
        var lviv = Obs(24.0, 49.84, 10); // ~800 km in 10 minutes
        var score = Correlator.Score(lviv, track, Shahed, 30);
        Assert.True(score.Total < 0.6, $"score {score.Total:F2}");
        Assert.Equal(0, score.Space);
    }

    [Fact]
    public void Different_specific_models_are_penalised()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.8, 50.9, 0, model: 10), T0);
        var other = Obs(34.8, 50.9, 5, model: 11);
        var score = Correlator.Score(other, track, Shahed, 30);
        Assert.Equal(0.2, score.Class);
    }

    [Fact]
    public void Opposite_direction_lowers_score()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.8, 50.9, 0, dir: 180), T0);
        var same = Correlator.Score(Obs(34.7, 50.5, 15, dir: 180), track, Shahed, 30);
        var opposite = Correlator.Score(Obs(34.7, 50.5, 15, dir: 0), track, Shahed, 30);
        Assert.True(same.Total > opposite.Total);
    }

    [Fact]
    public void Track_adopts_more_specific_model_and_builds_geometry()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.8, 50.9, 0), T0);
        Assert.Null(track.TrackGeometry);
        TrackUpdater.Apply(track, Obs(34.55, 49.59, 50, model: 10), T0.AddMinutes(50), isNewer: true);
        Assert.Equal(10, track.ThreatModelId);
        Assert.NotNull(track.TrackGeometry);
        Assert.Equal(2, track.TrackGeometry!.NumPoints);
        Assert.Equal(2, track.ObservationCount);
        // No reported direction: derived from movement, marked as low confidence.
        Assert.Equal(DirectionKind.TowardsPlace, track.DirectionKind);
        Assert.Equal(ConfidenceLevel.Low, track.DirectionConfidence);
        Assert.InRange(track.DirectionDeg!.Value, 170, 200);
    }

    [Fact]
    public void Older_observation_does_not_move_the_marker()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.55, 49.59, 50), T0);
        var last = track.LastLocation;
        TrackUpdater.Apply(track, Obs(34.8, 50.9, 0), T0, isNewer: false);
        Assert.Same(last, track.LastLocation);
        Assert.Equal(T0, track.FirstSeenAt);
        Assert.Equal(2, track.ObservationCount);
    }

    [Fact]
    public void Track_confidence_grows_with_corroboration()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.8, 50.9, 0), T0);
        track.DistinctSourceCount = 1;
        Assert.Equal(ConfidenceLevel.Medium, TrackUpdater.ComputeTrackConfidence(track, ConfidenceLevel.Medium));
        track.DistinctSourceCount = 2;
        Assert.Equal(ConfidenceLevel.High, TrackUpdater.ComputeTrackConfidence(track, ConfidenceLevel.Medium));
        Assert.Equal(ConfidenceLevel.High, TrackUpdater.ComputeTrackConfidence(track, ConfidenceLevel.High));
    }

    [Fact]
    public void Same_source_reporting_two_regions_minutes_apart_means_two_objects()
    {
        // Sumy region and Chernihiv region centroids are ~150 km apart; nothing covers that in 2 minutes.
        var first = Obs(32.0, 51.4, 0, place: 1);
        first.SourceId = 7;
        first.LocationAccuracyKm = 150; // region-level
        var track = TrackUpdater.CreateTrack(first, T0);
        var second = Obs(34.2, 51.0, 2, place: 2);
        second.SourceId = 7;
        second.LocationAccuracyKm = 150;
        Assert.True(Correlator.Score(second, track, Shahed, 30).Total < 0.6);
        // A different source saying the same thing is corroboration, not a split.
        second.SourceId = 8;
        Assert.True(Correlator.Score(second, track, Shahed, 30).Total >= 0.6);
    }

    [Fact]
    public void Repeated_origin_does_not_drag_the_track_back()
    {
        // Track already in Kyiv oblast; a new message only says "from Chernihiv oblast towards Kyiv".
        var track = TrackUpdater.CreateTrack(Obs(30.45, 50.30, 0, place: 5), T0);
        var next = Obs(32.0, 51.35, 10, place: 6, dir: 225);
        next.OriginPlaceId = 6;
        next.LocationAccuracyKm = 148; // a whole oblast
        TrackUpdater.Apply(track, next, T0.AddMinutes(10), isNewer: true);
        Assert.Equal(5, track.LastLocationPlaceId);
        Assert.Null(track.TrackGeometry);
        Assert.Equal(225, track.DirectionDeg); // the reported course still applies
    }

    [Fact]
    public void Precise_origin_is_a_position_and_moves_the_track()
    {
        // "з Броварів курсом на Київ": Brovary is a town, so the drone is there now.
        var track = TrackUpdater.CreateTrack(Obs(30.45, 50.30, 0, place: 5), T0);
        var next = Obs(30.79, 50.51, 10, place: 3932, dir: 248);
        next.OriginPlaceId = 3932;
        next.LocationAccuracyKm = 3;
        TrackUpdater.Apply(track, next, T0.AddMinutes(10), isNewer: true);
        Assert.Equal(3932, track.LastLocationPlaceId);
        Assert.NotNull(track.TrackGeometry);
    }

    [Fact]
    public void Adjacent_oblasts_form_a_path_but_no_derived_direction()
    {
        var first = Obs(34.8, 50.9, 0);
        first.LocationAccuracyKm = 147;
        var track = TrackUpdater.CreateTrack(first, T0);
        var next = Obs(34.55, 49.59, 30);
        next.LocationAccuracyKm = 135;
        TrackUpdater.Apply(track, next, T0.AddMinutes(30), isNewer: true);
        Assert.NotNull(track.TrackGeometry);
        Assert.Null(track.DirectionDeg);
    }

    [Fact]
    public void Movement_does_not_override_a_reported_direction()
    {
        var track = TrackUpdater.CreateTrack(Obs(34.8, 50.9, 0, dir: 225), T0);
        TrackUpdater.Apply(track, Obs(34.55, 49.59, 50), T0.AddMinutes(50), isNewer: true);
        Assert.Equal(225, track.DirectionDeg);
        Assert.Equal(DirectionKind.Compass, track.DirectionKind);
    }
}
