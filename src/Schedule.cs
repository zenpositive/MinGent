using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MinGent
{
    class ScheduledTask
    {
        public const string Daily = "daily", Weekdays = "weekdays", Weekly = "weekly", Monthly = "monthly", Once = "once";
        public string Id = "", Name = "", Prompt = "", Kind = Daily, Folder = "";
        public int Hour = 8, Minute, Day = 1;
        public DateTime Date = DateTime.MinValue, Created = DateTime.MinValue, LastRun = DateTime.MinValue;
        public bool On = true;

        public ScheduledTask Copy() { return (ScheduledTask)MemberwiseClone(); }
    }

    static class Schedule
    {
        public const int Most = 30;
        public static readonly string[] Kinds = { ScheduledTask.Daily, ScheduledTask.Weekdays, ScheduledTask.Weekly, ScheduledTask.Monthly, ScheduledTask.Once };
        public static readonly string[] KindNames = { "Every day", "Monday to Friday", "Every week", "Every month", "Once" };
        static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        const string Stamp = "yyyy-MM-dd HH:mm";

        public static string FilePath { get { return Path.Combine(Program.DataDir, "scheduled.json"); } }

        static DateTime At(DateTime day, ScheduledTask t) { return new DateTime(day.Year, day.Month, day.Day, t.Hour, t.Minute, 0); }

        public static DateTime Next(ScheduledTask t, DateTime after)
        {
            if (t.Kind == ScheduledTask.Once)
            {
                if (t.Date == DateTime.MinValue) return DateTime.MaxValue;
                DateTime once = At(t.Date, t);
                return once > after ? once : DateTime.MaxValue;
            }
            DateTime day = after.Date;
            for (int i = 0; i < 800; i++, day = day.AddDays(1))
            {
                bool fits;
                if (t.Kind == ScheduledTask.Weekdays) fits = day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday;
                else if (t.Kind == ScheduledTask.Weekly) fits = (int)day.DayOfWeek == t.Day;
                else if (t.Kind == ScheduledTask.Monthly) fits = day.Day == Math.Min(Math.Max(1, t.Day), DateTime.DaysInMonth(day.Year, day.Month));
                else fits = true;
                if (!fits) continue;
                DateTime at = At(day, t);
                if (at > after) return at;
            }
            return DateTime.MaxValue;
        }

        public static bool Due(ScheduledTask t, DateTime now)
        {
            if (!t.On || t.Prompt.Trim().Length == 0) return false;
            DateTime from = t.LastRun > t.Created ? t.LastRun : t.Created;
            return Next(t, from) <= now;
        }

        public static ScheduledTask FirstDue(List<ScheduledTask> tasks, DateTime now)
        {
            ScheduledTask first = null;
            DateTime best = DateTime.MaxValue;
            foreach (ScheduledTask t in tasks)
            {
                if (!Due(t, now)) continue;
                DateTime at = Next(t, t.LastRun > t.Created ? t.LastRun : t.Created);
                if (at < best) { best = at; first = t; }
            }
            return first;
        }

        public static void Ran(ScheduledTask t, DateTime now)
        {
            t.LastRun = now;
            if (t.Kind == ScheduledTask.Once) t.On = false;
        }

        static string Clock(ScheduledTask t) { return t.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + t.Minute.ToString("00", CultureInfo.InvariantCulture); }

        public static string When(ScheduledTask t)
        {
            switch (t.Kind)
            {
                case ScheduledTask.Weekdays: return "Monday to Friday at " + Clock(t);
                case ScheduledTask.Weekly: return "Every " + DayNames[Math.Max(0, Math.Min(6, t.Day))] + " at " + Clock(t);
                case ScheduledTask.Monthly: return t.Day >= 31 ? "On the last day of each month at " + Clock(t) : "On day " + t.Day.ToString(CultureInfo.InvariantCulture) + " of each month at " + Clock(t);
                case ScheduledTask.Once: return "Once, on " + (t.Date == DateTime.MinValue ? "a day not set yet" : t.Date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)) + " at " + Clock(t);
            }
            return "Every day at " + Clock(t);
        }

        public static string DayText(ScheduledTask t)
        {
            if (t.Kind == ScheduledTask.Weekly) return DayNames[Math.Max(0, Math.Min(6, t.Day))];
            if (t.Kind == ScheduledTask.Monthly) return Math.Max(1, Math.Min(31, t.Day)).ToString(CultureInfo.InvariantCulture);
            if (t.Kind == ScheduledTask.Once && t.Date != DateTime.MinValue) return t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return "";
        }

        public static bool TryTime(string text, out int hour, out int minute)
        {
            hour = minute = 0;
            string s = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            bool pm = s.EndsWith("pm", StringComparison.Ordinal), am = s.EndsWith("am", StringComparison.Ordinal);
            if (pm || am) s = s.Substring(0, s.Length - 2);
            string[] parts = s.Split(':', '.');
            if (parts.Length < 1 || parts.Length > 2 || parts[0].Length == 0 || parts[0].Length > 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour)) return false;
            if (parts.Length == 2 && (parts[1].Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute))) return false;
            if (pm || am)
            {
                if (hour < 1 || hour > 12) return false;
                hour = hour % 12 + (pm ? 12 : 0);
            }
            return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
        }

        public static string SetDay(ScheduledTask t, string text)
        {
            string s = (text ?? "").Trim();
            if (t.Kind == ScheduledTask.Weekly)
            {
                for (int i = 0; i < DayNames.Length; i++)
                    if (s.Length >= 2 && DayNames[i].StartsWith(s, StringComparison.OrdinalIgnoreCase)) { t.Day = i; return null; }
                return "For a task that runs every week, Day is a weekday such as Monday.";
            }
            if (t.Kind == ScheduledTask.Monthly)
            {
                int d;
                if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out d) && d >= 1 && d <= 31) { t.Day = d; return null; }
                return "For a task that runs every month, Day is a number from 1 to 31 (31 means the last day of the month).";
            }
            if (t.Kind == ScheduledTask.Once)
            {
                DateTime date;
                string[] forms = { "yyyy-MM-dd", "yyyy-M-d", "d.M.yyyy", "dd.MM.yyyy", "M/d/yyyy", "MM/dd/yyyy" };
                if (DateTime.TryParseExact(s, forms, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) { t.Date = date.Date; return null; }
                return "For a task that runs once, Day is a date such as " + DateTime.Now.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".";
            }
            return null;
        }

        public static string Check(ScheduledTask t, DateTime now)
        {
            if (t.Name.Trim().Length == 0) return "Give the task a name.";
            if (t.Prompt.Trim().Length == 0) return "Say what the task should do.";
            if (Array.IndexOf(Kinds, t.Kind) < 0) return "Choose how often the task runs.";
            if (t.Kind == ScheduledTask.Once && t.On && t.LastRun == DateTime.MinValue && Next(t, now) == DateTime.MaxValue) return "That day and time have already passed.";
            return null;
        }

        public static List<ScheduledTask> Load()
        {
            List<ScheduledTask> list = new List<ScheduledTask>();
            try
            {
                if (Program.Erased || !File.Exists(FilePath)) return list;
                List<object> items = Json.List(Json.Parse(File.ReadAllText(FilePath)), "tasks");
                if (items == null) return list;
                foreach (object o in items)
                {
                    ScheduledTask t = new ScheduledTask();
                    t.Id = Json.Str(o, "id") ?? "";
                    t.Name = Json.Str(o, "name") ?? "";
                    t.Prompt = Json.Str(o, "prompt") ?? "";
                    t.Folder = Json.Str(o, "folder") ?? "";
                    string kind = Json.Str(o, "kind") ?? "";
                    t.Kind = Array.IndexOf(Kinds, kind) >= 0 ? kind : ScheduledTask.Daily;
                    t.Hour = Math.Max(0, Math.Min(23, (int)Json.Num(o, "hour")));
                    t.Minute = Math.Max(0, Math.Min(59, (int)Json.Num(o, "minute")));
                    t.Day = Math.Max(0, Math.Min(31, (int)Json.Num(o, "day")));
                    t.Date = Time(Json.Str(o, "date"));
                    t.Created = Time(Json.Str(o, "created"));
                    t.LastRun = Time(Json.Str(o, "lastRun"));
                    t.On = !(Json.Get(o, "on") is bool) || (bool)Json.Get(o, "on");
                    if (t.Id.Length == 0) t.Id = Guid.NewGuid().ToString("N");
                    if (list.Count < Most) list.Add(t);
                }
            }
            catch { }
            return list;
        }

        static DateTime Time(string s)
        {
            DateTime d;
            return DateTime.TryParseExact(s ?? "", Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }

        static string Time(DateTime d) { return d == DateTime.MinValue ? "" : d.ToString(Stamp, CultureInfo.InvariantCulture); }

        public static bool Save(List<ScheduledTask> tasks)
        {
            if (Program.Erased) return false;
            try
            {
                List<object> items = new List<object>();
                foreach (ScheduledTask t in tasks)
                    items.Add(Json.O("id", t.Id, "name", t.Name, "prompt", t.Prompt, "kind", t.Kind, "hour", t.Hour, "minute", t.Minute, "day", t.Day,
                        "date", Time(t.Date), "created", Time(t.Created), "lastRun", Time(t.LastRun), "on", t.On, "folder", t.Folder ?? ""));
                Directory.CreateDirectory(Program.DataDir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(Json.O("tasks", items)), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
                return true;
            }
            catch { return false; }
        }
    }
}
