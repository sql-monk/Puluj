namespace Puluj.Infrastructure.Persistence.Sql;

/// <summary>
/// The probable predecessors of a target: every kinematic link behind it (not only the best one, as
/// puluj_target_chain does), followed for a few generations. Each row carries the link's own probability and
/// the product along the path, so the UI can draw a 55 % / 45 % fork and fade the second generation.
/// </summary>
public static class PredecessorsSql
{
    public const string Up = """
        CREATE OR REPLACE FUNCTION puluj_target_predecessors(p_target_id bigint, p_depth integer)
        RETURNS TABLE(generation integer, from_target_id bigint, to_target_id bigint, kind integer,
                      probability double precision, path_probability double precision)
        LANGUAGE sql STABLE AS $$
            WITH RECURSIVE g AS (
                SELECT 1 AS generation, l.from_target_id, l.to_target_id, l.kind, l.probability, l.probability AS path_probability
                FROM target_links l
                WHERE l.to_target_id = p_target_id AND l.kind <> 4
                UNION ALL
                SELECT g.generation + 1, l.from_target_id, l.to_target_id, l.kind, l.probability, g.path_probability * l.probability
                FROM g
                JOIN target_links l ON l.to_target_id = g.from_target_id AND l.kind <> 4
                WHERE g.generation < p_depth
            )
            SELECT generation, from_target_id, to_target_id, kind, probability, path_probability FROM g
        $$;
        """;

    public const string Down = """
        DROP FUNCTION IF EXISTS puluj_target_predecessors(bigint, integer);
        """;

    /// <summary>
    /// The family of a target: its ancestors up to p_depth generations (the same rows as puluj_target_predecessors,
    /// ancestral = true) and, for each ancestor, where else it could have flown — its other successors, followed down
    /// no further than the target's own generation (ancestral = false). With depth 2: parents and grandparents, the
    /// parents' other children (siblings), the grandparents' other children (uncles) and their children (cousins).
    /// Nothing else: a relative's own ancestry or later descendants is not part of the picture. Relatives whose path
    /// product falls under 2 % are left out: they would be hundreds of invisible legs.
    /// generation = the generation of the link's `from` node above the target (1 = a parent's link, 2 = a grandparent's).
    /// </summary>
    public const string FamilyUp = """
        CREATE OR REPLACE FUNCTION puluj_target_family(p_target_id bigint, p_depth integer)
        RETURNS TABLE(generation integer, from_target_id bigint, to_target_id bigint, kind integer,
                      probability double precision, path_probability double precision, ancestral boolean)
        LANGUAGE sql STABLE AS $$
            WITH RECURSIVE up AS (
                SELECT 1 AS generation, l.from_target_id, l.to_target_id, l.kind, l.probability, l.probability AS path_probability
                FROM target_links l
                WHERE l.to_target_id = p_target_id AND l.kind <> 4
                UNION ALL
                SELECT up.generation + 1, l.from_target_id, l.to_target_id, l.kind, l.probability, up.path_probability * l.probability
                FROM up
                JOIN target_links l ON l.to_target_id = up.from_target_id AND l.kind <> 4
                WHERE up.generation < p_depth
            ),
            anc AS (
                SELECT from_target_id AS target_id, MIN(generation) AS generation, MAX(path_probability) AS path_probability
                FROM up
                GROUP BY from_target_id
            ),
            down AS (
                SELECT a.generation - 1 AS level, l.from_target_id, l.to_target_id, l.kind, l.probability, a.path_probability * l.probability AS path_probability
                FROM anc a
                JOIN target_links l ON l.from_target_id = a.target_id AND l.kind <> 4
                WHERE l.to_target_id <> p_target_id
                  AND a.path_probability * l.probability >= 0.02
                  AND NOT EXISTS (SELECT 1 FROM up u WHERE u.from_target_id = l.from_target_id AND u.to_target_id = l.to_target_id)
                UNION ALL
                SELECT d.level - 1, l.from_target_id, l.to_target_id, l.kind, l.probability, d.path_probability * l.probability
                FROM down d
                JOIN target_links l ON l.from_target_id = d.to_target_id AND l.kind <> 4
                WHERE d.level > 0 AND d.path_probability * l.probability >= 0.02
            )
            SELECT generation, from_target_id, to_target_id, kind, probability, path_probability, true FROM up
            UNION ALL
            SELECT level + 1, from_target_id, to_target_id, kind, probability, path_probability, false FROM down
        $$;
        """;

    public const string FamilyDown = """
        DROP FUNCTION IF EXISTS puluj_target_family(bigint, integer);
        """;
}
