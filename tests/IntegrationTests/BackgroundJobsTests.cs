using System.Net;
using Aiursoft.Canon.TaskQueue;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class BackgroundJobsTests : TestBase
{
    private async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = Environment.TickCount;
        while (!condition() && Environment.TickCount - start < timeoutMs)
            await Task.Delay(50);
    }

    [TestMethod]
    public async Task JobQueueBasicOperationsTest()
    {
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();
        Assert.AreEqual(0, queue.GetPendingTasks().Count());
        Assert.AreEqual(0, queue.GetProcessingTasks().Count());

        var jobCompleted = false;
        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Test Queue",
            taskName: "Test Job 1",
            task: async (_) =>
            {
                await Task.Delay(100);
                jobCompleted = true;
            });

        var pendingTasks = queue.GetPendingTasks().ToList();
        Assert.HasCount(1, pendingTasks);
        Assert.AreEqual("Test Queue", pendingTasks[0].QueueName);
        Assert.AreEqual("Test Job 1", pendingTasks[0].TaskName);

        await WaitUntil(() => jobCompleted);

        Assert.IsTrue(jobCompleted);
        var recentTasks = queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).ToList();
        Assert.IsTrue(recentTasks.Any(t => t.TaskName == "Test Job 1"));
    }

    [TestMethod]
    public async Task JobQueueParallelExecutionTest()
    {
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();
        var queueAStartTime = DateTime.MinValue;
        var queueBStartTime = DateTime.MinValue;
        var queueACompleted = false;
        var queueBCompleted = false;

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Queue A", "Job A1", async (_) =>
        {
            queueAStartTime = DateTime.UtcNow;
            await Task.Delay(500);
            queueACompleted = true;
        });

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Queue B", "Job B1", async (_) =>
        {
            queueBStartTime = DateTime.UtcNow;
            await Task.Delay(500);
            queueBCompleted = true;
        });

        await WaitUntil(() => queueACompleted && queueBCompleted);

        Assert.IsTrue(queueACompleted);
        Assert.IsTrue(queueBCompleted);

        var timeDifference = Math.Abs((queueAStartTime - queueBStartTime).TotalMilliseconds);
        Assert.IsLessThan(200, timeDifference, $"Tasks should start in parallel, but time difference was {timeDifference}ms");
    }

    [TestMethod]
    public async Task JobQueueSequentialExecutionInSameQueueTest()
    {
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();
        var job1StartTime = DateTime.MinValue;
        var job2StartTime = DateTime.MinValue;
        var job1Completed = false;
        var job2Completed = false;

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Sequential Queue", "Sequential Job 1", async (_) =>
        {
            job1StartTime = DateTime.UtcNow;
            await Task.Delay(500);
            job1Completed = true;
        });

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Sequential Queue", "Sequential Job 2", async (_) =>
        {
            job2StartTime = DateTime.UtcNow;
            await Task.Delay(500);
            job2Completed = true;
        });

        await WaitUntil(() => job1Completed && job2Completed, timeoutMs: 4000);

        Assert.IsTrue(job1Completed);
        Assert.IsTrue(job2Completed);

        var timeDifference = (job2StartTime - job1StartTime).TotalMilliseconds;
        Assert.IsGreaterThanOrEqualTo(400, timeDifference, $"Job 2 should start after Job 1 completes, but time difference was only {timeDifference}ms");
    }

    [TestMethod]
    public async Task JobCancellationTest()
    {
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();

        // Add a blocking task so the second one stays pending
        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Cancellation Test Queue", "Blocking Job",
            async (_) => await Task.Delay(2000));

        var jobExecuted = false;
        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Cancellation Test Queue", "Cancellable Job",
            async (_) =>
            {
                await Task.Delay(5000);
                jobExecuted = true;
            });

        // Wait for the cancellable task to appear in pending
        await WaitUntil(() => queue.GetPendingTasks().Any(t => t.TaskName == "Cancellable Job"), timeoutMs: 1000);
        var pendingTasks = queue.GetPendingTasks().ToList();
        var cancellableTask = pendingTasks.FirstOrDefault(t => t.TaskName == "Cancellable Job");
        Assert.IsNotNull(cancellableTask, "Cancellable task should be in pending queue");

        var cancelled = queue.CancelTask(cancellableTask.TaskId);
        Assert.IsTrue(cancelled);

        // Wait for the blocking task to finish and let the worker process cancellation
        await WaitUntil(() =>
        {
            var all = queue.GetAllTasks().ToList();
            return all.Any(t => t.TaskId == cancellableTask.TaskId && t.Status == TaskExecutionStatus.Cancelled);
        }, timeoutMs: 5000);

        Assert.IsFalse(jobExecuted);

        var allTasks = queue.GetAllTasks().ToList();
        var cancelledTaskInfo = allTasks.FirstOrDefault(t => t.TaskId == cancellableTask.TaskId);
        Assert.IsNotNull(cancelledTaskInfo);
        Assert.AreEqual(TaskExecutionStatus.Cancelled, cancelledTaskInfo.Status);
    }

    [TestMethod]
    public async Task JobFailureHandlingTest()
    {
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Failure Test Queue", "Failing Job",
            async (_) =>
            {
                await Task.Delay(100);
                throw new Exception("Intentional test failure");
            });

        await WaitUntil(() =>
        {
            var recent = queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).ToList();
            return recent.Any(t => t.TaskName == "Failing Job" && t.Status == TaskExecutionStatus.Failed);
        });

        var recentTasks = queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).ToList();
        var failedTask = recentTasks.FirstOrDefault(t => t.TaskName == "Failing Job");
        Assert.IsNotNull(failedTask);
        Assert.AreEqual(TaskExecutionStatus.Failed, failedTask.Status);
        Assert.IsTrue(failedTask.ErrorMessage?.Contains("Intentional test failure"));
    }

    [TestMethod]
    public async Task JobsPageAccessRequiresAuthenticationTest()
    {
        var response = await Http.GetAsync("/Jobs");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.OriginalString.Contains("/Account/Login"));
    }

    [TestMethod]
    public async Task JobsPageAccessWithAdminTest()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Jobs");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("table", html);
    }

    [TestMethod]
    public async Task CreateTestJobViaUITest()
    {
        await LoginAsAdmin();
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();
        var initialTaskCount = queue.GetAllTasks().Count();

        var triggerResponse = await PostForm("/Jobs/Trigger", new Dictionary<string, string>
        {
            { "jobTypeName", "DummyJob" }
        }, tokenUrl: "/Jobs");

        Assert.AreEqual(HttpStatusCode.Found, triggerResponse.StatusCode);
        var redirectUrl = triggerResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(redirectUrl == "/Jobs/Index" || redirectUrl == "/Jobs");

        await WaitUntil(() => queue.GetAllTasks().Count() > initialTaskCount);

        var tasks = queue.GetAllTasks().ToList();
        var dummyTask = tasks.FirstOrDefault(t => t.QueueName == "DummyJob");
        Assert.IsNotNull(dummyTask);
        Assert.AreEqual(TaskTriggerSource.Manual, dummyTask.TriggerSource);
    }

    [TestMethod]
    public async Task CreateBothJobsViaUIAndVerifyParallelExecutionTest()
    {
        await LoginAsAdmin();
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();

        await PostForm("/Jobs/Trigger", new Dictionary<string, string>
        {
            { "jobTypeName", "DummyJob" }
        }, tokenUrl: "/Jobs");

        await PostForm("/Jobs/Trigger", new Dictionary<string, string>
        {
            { "jobTypeName", "OrphanAvatarCleanupJob" }
        }, tokenUrl: "/Jobs");

        await WaitUntil(() =>
        {
            var tasks = queue.GetAllTasks().ToList();
            return tasks.Any(t => t.QueueName == "DummyJob") &&
                   tasks.Any(t => t.QueueName == "OrphanAvatarCleanupJob");
        });

        var tasksList = queue.GetAllTasks().ToList();
        Assert.IsNotEmpty(tasksList.Where(t => t.QueueName == "DummyJob").ToList());
        Assert.IsNotEmpty(tasksList.Where(t => t.QueueName == "OrphanAvatarCleanupJob").ToList());
    }

    [TestMethod]
    public async Task TestCleanupOldTasks()
    {
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();

        var taskId = queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            "Cleanup Queue", "Cleanup Job",
            async (_) => await Task.CompletedTask);

        await WaitUntil(() => queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).Any(t => t.TaskId == taskId));

        var completedTask = queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).FirstOrDefault(t => t.TaskId == taskId);
        Assert.IsNotNull(completedTask);

        queue.CleanupOldCompletedTasks(TimeSpan.FromSeconds(-1));

        var allTasks = queue.GetAllTasks().ToList();
        Assert.IsFalse(allTasks.Any(t => t.TaskId == taskId));
    }

    [TestMethod]
    public void TestQueueWithDependencyDefaultName()
    {
        var queue = Server.Services.GetRequiredService<ServiceTaskQueue>();
        var taskId = queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(async (_) => await Task.CompletedTask);

        var task = queue.GetAllTasks().FirstOrDefault(t => t.TaskId == taskId);
        Assert.IsNotNull(task);
        Assert.AreEqual(typeof(ILogger<BackgroundJobsTests>).Name, task.QueueName);
    }
}
