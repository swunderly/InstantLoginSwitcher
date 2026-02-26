using InstantLoginSwitcher.Core.Services;

namespace InstantLoginSwitcher.Core.Tests;

public sealed class TaskSchedulerServiceTests
{
    private readonly TaskSchedulerService _service = new();

    [Fact]
    public void GetTaskNameForUser_AddsStableHashSuffix()
    {
        var taskName1 = _service.GetTaskNameForUser("User Name");
        var taskName2 = _service.GetTaskNameForUser("User Name");

        Assert.Equal(taskName1, taskName2);
        Assert.StartsWith(TaskSchedulerService.TaskPrefix, taskName1, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^[A-Za-z0-9\-_]+_[A-F0-9]{8}$", taskName1[TaskSchedulerService.TaskPrefix.Length..]);
    }

    [Fact]
    public void GetTaskNameForUser_DistinguishesSanitizedCollisions()
    {
        var first = _service.GetTaskNameForUser("user-a");
        var second = _service.GetTaskNameForUser("user_a");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GetTaskNameForUser_IsCaseInsensitive()
    {
        var lower = _service.GetTaskNameForUser("swunderly");
        var upper = _service.GetTaskNameForUser("SWUNDERLY");

        Assert.Equal(lower, upper);
    }
}
