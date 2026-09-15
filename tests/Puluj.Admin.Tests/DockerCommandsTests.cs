using Puluj.Admin.Docker;

namespace Puluj.Admin.Tests;

public class DockerCommandsTests
{
    [Fact]
    public void Scale_builds_the_compose_command_with_every_file_and_the_safety_flags()
    {
        var args = DockerCommands.Scale("puluj", "/deploy", ["docker-compose.yml", "docker-compose.override.yml"], ".env", "processor", 3);

        Assert.Equal(
        [
            "compose", "-p", "puluj", "--project-directory", "/deploy",
            "-f", Path.Combine("/deploy", "docker-compose.yml"),
            "-f", Path.Combine("/deploy", "docker-compose.override.yml"),
            "--env-file", Path.Combine("/deploy", ".env"),
            "up", "-d", "--no-build", "--no-deps", "--no-recreate", "--scale", "processor=3", "processor",
        ], args);
    }

    [Fact]
    public void Scale_without_env_file_skips_the_option()
    {
        var args = DockerCommands.Scale("puluj", "/deploy", ["docker-compose.yml"], null, "processor", 0);

        Assert.DoesNotContain("--env-file", args);
        Assert.Contains("processor=0", args);
    }

    [Fact]
    public void ComposeFiles_default_adds_the_override_only_when_it_exists()
    {
        Assert.Equal(["docker-compose.yml"], DockerCommands.ComposeFiles([], _ => false));
        Assert.Equal(["docker-compose.yml", "docker-compose.override.yml"], DockerCommands.ComposeFiles([], f => f == "docker-compose.override.yml"));
        Assert.Equal(["custom.yml"], DockerCommands.ComposeFiles(["custom.yml"], _ => true));
    }

    [Fact]
    public void Ps_filters_by_the_project_label_and_asks_for_json()
    {
        Assert.Equal(["ps", "-a", "--filter", "label=com.docker.compose.project=puluj", "--format", "{{json .}}"], DockerCommands.Ps("puluj"));
        Assert.Equal(["stats", "--no-stream", "--format", "{{json .}}"], DockerCommands.Stats());
    }

    [Theory]
    [InlineData("restart")]
    [InlineData("stop")]
    [InlineData("start")]
    public void Action_is_verb_then_id(string verb)
    {
        Assert.Equal([verb, "abc123"], DockerCommands.Action("abc123", verb));
    }

    [Fact]
    public void Action_refuses_anything_else()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DockerCommands.Action("abc123", "rm"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DockerCommands.Action("abc123", "kill"));
    }
}
