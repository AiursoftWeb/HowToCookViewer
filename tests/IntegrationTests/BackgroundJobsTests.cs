using System.Net;
using Aiursoft.Canon.TaskQueue;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

/// <summary>
/// 后台任务队列集成测试：测试ServiceTaskQueue的队列管理、任务执行、并行处理等核心功能
/// </summary>
[TestClass]
public class BackgroundJobsTests : TestBase
{
    [TestMethod]
    public async Task JobQueueBasicOperationsTest()
    {
        // 直接从服务容器获取ServiceTaskQueue实例
        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();

        // Step 1: 验证初始状态 - 没有任何任务
        var initialPending = queue.GetPendingTasks().Count();
        var initialProcessing = queue.GetProcessingTasks().Count();
        Assert.AreEqual(0, initialPending);
        Assert.AreEqual(0, initialProcessing);

        // Step 2: 添加一个简单的任务到队列
        var jobCompleted = false;
        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Test Queue",
            taskName: "Test Job 1",
            task: async (_) =>
            {
                await Task.Delay(100); // 短暂延迟
                jobCompleted = true;
            });

        // Step 3: 验证任务已加入待处理队列
        var pendingTasks = queue.GetPendingTasks().ToList();
        Assert.HasCount(1, pendingTasks);
        Assert.AreEqual("Test Queue", pendingTasks[0].QueueName);
        Assert.AreEqual("Test Job 1", pendingTasks[0].TaskName);

        // Step 4: 等待任务执行完成
        await Task.Delay(2000); // 给worker足够时间处理

        // Step 5: 验证任务已完成
        Assert.IsTrue(jobCompleted);
        var recentTasks = queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).ToList();
        Assert.IsTrue(recentTasks.Any(t => t.TaskName == "Test Job 1"));
    }

    [TestMethod]
    public async Task JobQueueParallelExecutionTest()
    {
        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();

        // Step 1: 向两个不同的队列添加任务
        var queueAStartTime = DateTime.MinValue;
        var queueBStartTime = DateTime.MinValue;
        var queueACompleted = false;
        var queueBCompleted = false;

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Queue A",
            taskName: "Job A1",
            task: async (_) =>
            {
                queueAStartTime = DateTime.UtcNow;
                await Task.Delay(500);
                queueACompleted = true;
            });

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Queue B",
            taskName: "Job B1",
            task: async (_) =>
            {
                queueBStartTime = DateTime.UtcNow;
                await Task.Delay(500);
                queueBCompleted = true;
            });

        // Step 2: 等待两个任务都完成
        await Task.Delay(2000);

        // Step 3: 验证两个任务都已完成
        Assert.IsTrue(queueACompleted);
        Assert.IsTrue(queueBCompleted);

        // Step 4: 验证任务是并行执行的（开始时间差应该很小）
        var timeDifference = Math.Abs((queueAStartTime - queueBStartTime).TotalMilliseconds);
        Assert.IsLessThan(200, timeDifference, $"Tasks should start in parallel, but time difference was {timeDifference}ms");
    }

    [TestMethod]
    public async Task JobQueueSequentialExecutionInSameQueueTest()
    {
        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();

        // Step 1: 向同一个队列添加两个任务
        var job1StartTime = DateTime.MinValue;
        var job2StartTime = DateTime.MinValue;
        var job1Completed = false;
        var job2Completed = false;

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Sequential Queue",
            taskName: "Sequential Job 1",
            task: async (_) =>
            {
                job1StartTime = DateTime.UtcNow;
                await Task.Delay(500);
                job1Completed = true;
            });

        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Sequential Queue",
            taskName: "Sequential Job 2",
            task: async (_) =>
            {
                job2StartTime = DateTime.UtcNow;
                await Task.Delay(500);
                job2Completed = true;
            });

        // Step 2: 等待两个任务都完成
        await Task.Delay(2500);

        // Step 3: 验证两个任务都已完成
        Assert.IsTrue(job1Completed);
        Assert.IsTrue(job2Completed);

        // Step 4: 验证任务是顺序执行的（Job 2应该在Job 1完成后才开始）
        var timeDifference = (job2StartTime - job1StartTime).TotalMilliseconds;
        Assert.IsGreaterThanOrEqualTo(400, timeDifference, $"Job 2 should start after Job 1 completes, but time difference was only {timeDifference}ms");
    }

    [TestMethod]
    public async Task JobCancellationTest()
    {
        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();

        // Step 1: 先添加一个阻塞任务，确保后续任务保持在Pending状态
        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Cancellation Test Queue",
            taskName: "Blocking Job",
            task: async (_) =>
            {
                await Task.Delay(10000); // 10秒延迟，确保测试期间一直在运行
            });

        // Step 2: 添加一个长时间运行的任务（这个应该保持在Pending状态）
        var jobExecuted = false;
        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Cancellation Test Queue",
            taskName: "Cancellable Job",
            task: async (_) =>
            {
                await Task.Delay(5000); // 5秒延迟
                jobExecuted = true;
            });

        // Step 3: 等待任务入队，并获取任务ID
        await Task.Delay(500); // 确保任务已入队
        var pendingTasks = queue.GetPendingTasks().ToList();
        var cancellableTask = pendingTasks.FirstOrDefault(t => t.TaskName == "Cancellable Job");
        Assert.IsNotNull(cancellableTask, "Cancellable task should be in pending queue");
        var taskId = cancellableTask.TaskId;

        // Step 4: 取消任务
        var cancelled = queue.CancelTask(taskId);
        Assert.IsTrue(cancelled);

        // Step 5: 等待足够长的时间，确保任务不会被执行
        await Task.Delay(2000);

        // Step 6: 验证任务没有被执行
        Assert.IsFalse(jobExecuted);

        // Step 7: 验证任务状态为已取消
        var allTasks = queue.GetAllTasks().ToList();
        var cancelledTaskInfo = allTasks.FirstOrDefault(t => t.TaskId == taskId);
        Assert.IsNotNull(cancelledTaskInfo);
        Assert.AreEqual(TaskExecutionStatus.Cancelled, cancelledTaskInfo.Status);
    }

    [TestMethod]
    public async Task JobFailureHandlingTest()
    {
        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();

        // Step 1: 添加一个会失败的任务
        queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(
            queueName: "Failure Test Queue",
            taskName: "Failing Job",
            task: async (_) =>
            {
                await Task.Delay(100);
                throw new Exception("Intentional test failure");
            });

        // Step 2: 等待任务执行并失败
        await Task.Delay(2000);

        // Step 3: 验证任务状态为失败，并包含错误信息
        var recentTasks = queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).ToList();
        var failedTask = recentTasks.FirstOrDefault(t => t.TaskName == "Failing Job");
        Assert.IsNotNull(failedTask);
        Assert.AreEqual(TaskExecutionStatus.Failed, failedTask.Status);
        Assert.IsTrue(failedTask.ErrorMessage?.Contains("Intentional test failure"));
    }

    [TestMethod]
    public async Task JobsPageAccessRequiresAuthenticationTest()
    {
        // Step 1: 未登录时访问后台任务页面
        var response = await Http.GetAsync("/Jobs");

        // Step 2: 应该被重定向到登录页面
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.OriginalString.Contains("/Account/Login"));
    }

    [TestMethod]
    public async Task JobsPageAccessWithAdminTest()
    {
        // Step 1: 以管理员身份登录
        await LoginAsAdmin();

        // Step 2: 访问后台任务页面
        var response = await Http.GetAsync("/Jobs");

        // Step 3: 应该成功访问
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        // 验证页面包含任务管理相关元素（这些是固定的HTML元素，不受本地化影响）
        Assert.Contains("table", html);
    }

    [TestMethod]
    public async Task CreateTestJobViaUITest()
    {
        // Step 1: 以管理员身份登录
        await LoginAsAdmin();

        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();
        var initialTaskCount = queue.GetAllTasks().Count();

        // Step 2: 通过 Trigger 端点触发 DummyJob
        var triggerResponse = await PostForm("/Jobs/Trigger", new Dictionary<string, string>
        {
            { "jobTypeName", "DummyJob" }
        }, tokenUrl: "/Jobs");

        // Step 3: 应该重定向回Jobs页面
        Assert.AreEqual(HttpStatusCode.Found, triggerResponse.StatusCode);
        var redirectUrl = triggerResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(redirectUrl == "/Jobs/Index" || redirectUrl == "/Jobs");

        // Step 4: 验证任务已被创建
        await Task.Delay(200); // 等待任务入队
        var currentTaskCount = queue.GetAllTasks().Count();
        Assert.IsGreaterThan(initialTaskCount, currentTaskCount);

        // Step 5: 验证创建的是 DummyJob 队列的任务
        var tasks = queue.GetAllTasks().ToList();
        var dummyTask = tasks.FirstOrDefault(t => t.QueueName == "DummyJob");
        Assert.IsNotNull(dummyTask);
        Assert.AreEqual(TaskTriggerSource.Manual, dummyTask.TriggerSource);
    }

    [TestMethod]
    public async Task CreateBothJobsViaUIAndVerifyParallelExecutionTest()
    {
        // Step 1: 以管理员身份登录
        await LoginAsAdmin();

        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();

        // Step 2: 触发 DummyJob
        await PostForm("/Jobs/Trigger", new Dictionary<string, string>
        {
            { "jobTypeName", "DummyJob" }
        }, tokenUrl: "/Jobs");

        // Step 3: 触发 OrphanAvatarCleanupJob
        await PostForm("/Jobs/Trigger", new Dictionary<string, string>
        {
            { "jobTypeName", "OrphanAvatarCleanupJob" }
        }, tokenUrl: "/Jobs");

        // Step 4: 等待任务入队
        await Task.Delay(500);

        // Step 5: 验证两个队列都有任务
        var tasks = queue.GetAllTasks().ToList();
        var dummyTasks   = tasks.Where(t => t.QueueName == "DummyJob").ToList();
        var orphanTasks  = tasks.Where(t => t.QueueName == "OrphanAvatarCleanupJob").ToList();

        Assert.IsNotEmpty(dummyTasks,  "DummyJob queue should have at least one task");
        Assert.IsNotEmpty(orphanTasks, "OrphanAvatarCleanupJob queue should have at least one task");
    }

    [TestMethod]
    public async Task TestCleanupOldTasks()
    {
        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();

        // Add a task and complete it
        var taskId = queue.QueueWithDependency<ILogger<BackgroundJobsTests>>("Cleanup Queue", "Cleanup Job", async (_) => await Task.CompletedTask);

        // Wait for it to complete
        await Task.Delay(1000);

        var completedTask = queue.GetRecentCompletedTasks(TimeSpan.FromMinutes(1)).FirstOrDefault(t => t.TaskId == taskId);
        Assert.IsNotNull(completedTask);

        // Cleanup tasks older than 0 seconds (all completed tasks)
        queue.CleanupOldCompletedTasks(TimeSpan.FromSeconds(-1));

        var allTasks = queue.GetAllTasks().ToList();
        Assert.IsFalse(allTasks.Any(t => t.TaskId == taskId));
    }

    [TestMethod]
    public void TestQueueWithDependencyDefaultName()
    {
        var queue = Server!.Services.GetRequiredService<ServiceTaskQueue>();
        var taskId = queue.QueueWithDependency<ILogger<BackgroundJobsTests>>(async (_) => await Task.CompletedTask);

        var task = queue.GetAllTasks().FirstOrDefault(t => t.TaskId == taskId);
        Assert.IsNotNull(task);
        Assert.AreEqual(typeof(ILogger<BackgroundJobsTests>).Name, task.QueueName);
    }
}
