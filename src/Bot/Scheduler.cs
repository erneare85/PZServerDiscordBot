using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

public class ScheduleItem
{
    private int _executeGate;

    public string               Name            { get; }
    public string               DisplayName     { get; }
    public Func<List<object>, Task> Function    { get; }
    public List<object>         Args            { get; set; }
    public ulong                IntervalMS      { get; set; }
    public DateTime             NextExecuteTime { get; set; }

    public ScheduleItem(string name, string displayName, ulong intervalMS, Func<List<object>, Task> func, List<object> args)
    {
        Name           = name;
        DisplayName    = displayName;
        Function       = func;
        Args           = args;

        UpdateInterval(intervalMS);
    }

    public void UpdateInterval(ulong intervalMS=0)
    {
        if(intervalMS > 0) IntervalMS = intervalMS;
        NextExecuteTime = DateTime.Now.AddMilliseconds(IntervalMS);
    }

    internal bool TryBeginExecute() => Interlocked.CompareExchange(ref _executeGate, 1, 0) == 0;

    internal void EndExecute() => Interlocked.Exchange(ref _executeGate, 0);
}

public static class Scheduler
{
    private static Timer clock;
    private static readonly object scheduleLock = new object();
    private static readonly List<ScheduleItem> scheduleItems = new List<ScheduleItem>();

    public static void Start(ulong intervalMS)
    {
        clock = new Timer
        {
            Interval = intervalMS
        };
        clock.Elapsed += ClockElapsed;
        clock.Start();
    }

    public static void Stop()
    {
        clock.Elapsed -= ClockElapsed;
        clock.Stop();
    }

    public static void AddItem(ScheduleItem item)
    {
        if(item.IntervalMS != 0)
            lock (scheduleLock)
                scheduleItems.Add(item);
    }

    public static void RemoveItem(string name)
    { 
        lock (scheduleLock)
        {
            int index = scheduleItems.FindIndex(item => item.Name == name);
            if(index != -1) scheduleItems.RemoveAt(index);
        }
    }

    public static ScheduleItem GetItem(string name)
    {
        lock (scheduleLock)
            return scheduleItems.Find(item => item.Name == name);
    }

    public static IReadOnlyCollection<ScheduleItem> GetItems()
    {
        lock (scheduleLock)
            return scheduleItems.ToList().AsReadOnly();
    }

    private static void ClockElapsed(object sender, ElapsedEventArgs e)
    {
        DateTime now = DateTime.Now;
        List<ScheduleItem> snapshot;
        lock (scheduleLock)
            snapshot = scheduleItems.ToList();

        foreach(ScheduleItem item in snapshot)
        {
            if(item.IntervalMS != 0
            && now >= item.NextExecuteTime
            && item.TryBeginExecute())
            {
                _ = RunItemAsync(item);
            }
        }
    }

    private static async Task RunItemAsync(ScheduleItem item)
    {
        try
        {
            await item.Function(item.Args).ConfigureAwait(false);
        }
        catch(Exception ex) 
        { 
            string exceptionMessage = $"Exception occured in ScheduleItem callback function. ScheduleItem: {item.Name}";

            if(ex is AggregateException aggregateEx)
            {
                int i=0;
                foreach(Exception innerEx in aggregateEx.InnerExceptions)
                    Logger.LogException(innerEx, $"{exceptionMessage}\n(THIS IS AN INNER EXCEPTION, NUMBER {++i})");
            }
            else Logger.LogException(ex, exceptionMessage);
        }
        finally
        {
            item.UpdateInterval();
            item.EndExecute();
        }
    }

    public static void Dispose()
    {
        if(clock != null)
            clock.Dispose();
    }

    public static uint GetIntervalFromTimes(List<string> scheduleTimes)
    {
        scheduleTimes.Sort();

        DateTime now = DateTime.Now;
		string nowString = now.ToString("HH:mm");

        DateTime nextRestartTimeDT = new DateTime();
        string nextRestartTime = "";

        if (scheduleTimes.Count == 0) return 999999999;
        
		foreach (string time in scheduleTimes)
		{
            DateTime timeDT;
            try 
	        {	        
		        timeDT = DateTime.Parse(time);
	        }
	        catch (Exception)
	        {
                Logger.WriteLog(string.Format("Scheduler.GetIntervalFromTimes() - ERROR: \"{0}\" is an invalid time.", time));
                continue;
	        }

			if (DateTime.Compare(timeDT, now) > 0)
			{
				nextRestartTimeDT = timeDT;
                break;
			}
		}

        try 
	    {	
            TimeSpan interval;

            if (nextRestartTimeDT == DateTime.MinValue)
            {
                interval = DateTime.Parse(scheduleTimes[0]).AddDays(1) - DateTime.Now;
                nextRestartTime = nextRestartTimeDT.ToString("HH:mm");
                nextRestartTime = string.Format("Tomorrow, {0}", scheduleTimes[0]);
            }
            else
            {
                interval = nextRestartTimeDT - DateTime.Parse(nowString);
            }

            Logger.WriteLog(string.Format("[Scheduler.GetIntervalFromTimes] - Next Restart Time: {0}", nextRestartTime));
            return Convert.ToUInt32(interval.TotalMilliseconds);
	    }
	    catch (Exception)
	    {
		    Logger.WriteLog(string.Format("[Scheduler.GetIntervalFromTimes] - Error. Next restart time: {0}, Current time: {1}", nextRestartTime, nowString));
            return 4294967295;
	    }
    }
}
