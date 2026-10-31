using System.Runtime.InteropServices;
using System.Xml;
using Microsoft.CSharp.RuntimeBinder;
using RoninVitals.Models;
using static RoninVitals.Services.CheckText;

namespace RoninVitals.Services;

/// <summary>
/// Read-only walk through every task in the Windows Task Scheduler library, using Windows' own Task Scheduler COM interface
/// (<c>Schedule.Service</c>), the same source as the Task Scheduler window and <c>Get-ScheduledTask</c>. It never creates,
/// changes, disables, deletes or runs a task. Judging the tasks is done in <see cref="ScheduledTaskRules"/>.
/// </summary>
public static class ScheduledTaskDiagnosticsService
{
    private const string TechCommand = "Get-ScheduledTask | Get-ScheduledTaskInfo   (each task's schedule is read from its XML: Export-ScheduledTask)";
    private const int IncludeHiddenTasks = 1;
    private const int NoFlags = 0;

    private sealed class TaskLibraryScan
    {
        public List<ScheduledTask> Tasks { get; } = [];
        public int Unreadable { get; set; }
    }

    public static CheckResult CheckScheduledTasks()
    {
        try
        {
            var scan = ReadTaskLibrary();
            var scheduledCount = scan.Tasks.Count(task => task.IsScheduled);
            var detail = $"Read {scan.Tasks.Count} tasks from the Task Scheduler library: {scheduledCount} scheduled, " +
                         $"{scan.Tasks.Count - scheduledCount} turned off or run only on demand, {scan.Unreadable} couldn't be read.";

            return ScheduledTaskRules.Judge(scan.Tasks, scan.Unreadable) with
            {
                TechCommand = TechCommand,
                TechDetail = detail,
                Tables = ScheduledTaskRules.BuildTables(scan.Tasks),
            };
        }
        catch (Exception exception) when (exception is COMException or RuntimeBinderException or InvalidCastException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't open the Windows Task Scheduler to read its tasks.", TechCommand, DescribeException(exception));
        }
    }

    private static TaskLibraryScan ReadTaskLibrary()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service") ?? throw new COMException("The Task Scheduler service isn't registered on this PC.");
        dynamic service = Activator.CreateInstance(serviceType) ?? throw new COMException("The Task Scheduler service didn't start.");
        var scan = new TaskLibraryScan();
        try
        {
            service.Connect();
            ReadFolder(service.GetFolder("\\"), scan);
        }
        finally
        {
            Release(service);
        }
        return scan;
    }

    private static void ReadFolder(dynamic folder, TaskLibraryScan scan)
    {
        try
        {
            dynamic registeredTasks = folder.GetTasks(IncludeHiddenTasks);
            try
            {
                foreach (dynamic registered in registeredTasks)
                {
                    try { scan.Tasks.Add(ReadTask(registered)); }
                    catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or XmlException or RuntimeBinderException) { scan.Unreadable++; }
                    finally { Release(registered); }
                }
            }
            finally { Release(registeredTasks); }

            dynamic subfolders = folder.GetFolders(NoFlags);
            try
            {
                foreach (dynamic subfolder in subfolders)
                {
                    try { ReadFolder(subfolder, scan); }
                    finally { Release(subfolder); }
                }
            }
            finally { Release(subfolders); }
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            scan.Unreadable++; // a folder Windows refused to list
        }
    }

    private static ScheduledTask ReadTask(dynamic registered)
    {
        string path = registered.Path;
        string name = registered.Name;
        bool isEnabled = registered.Enabled;
        DateTime lastRun = registered.LastRunTime;
        DateTime nextRun = registered.NextRunTime;
        int lastResult = registered.LastTaskResult;
        string xml = registered.Xml;

        return ScheduledTaskRules.Parse(path, name, isEnabled, lastRun, nextRun, lastResult, xml, IndirectString.Resolve, AccountNames.Resolve);
    }

    private static void Release(object comObject)
    {
        if (Marshal.IsComObject(comObject)) Marshal.FinalReleaseComObject(comObject);
    }
}
