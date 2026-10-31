using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    static class YouTube
    {
        static readonly Regex VideoUrl = new Regex(
            @"^https?://(?:www\.|m\.|music\.)?(?:youtube\.com/(?:watch/?\?(?:[^#]*&)?v=|shorts/|live/|embed/|v/)|youtu\.be/|youtube-nocookie\.com/embed/)([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public const int PartChars = 24000;

        public class Track
        {
            public string Url = "", Lang = "", Name = "";
            public bool Auto;
        }

        public class Video
        {
            public string Id = "", Title = "", Channel = "", Published = "", Description = "", Status = "", Reason = "", ApiKey;
            public int Seconds;
            public long Views;
            public readonly List<Track> Tracks = new List<Track>();
        }

        public static string VideoId(string url)
        {
            Match m = VideoUrl.Match((url ?? "").Trim());
            return m.Success ? m.Groups[1].Value : null;
        }

        static Dictionary<string, string> Headers()
        {
            Dictionary<string, string> h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            h["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";
            h["Accept-Language"] = "en-US,en;q=0.8";
            h["Cookie"] = "SOCS=CAI";
            return h;
        }

        public static object ObjectAfter(string text, string marker)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int at = 0;
            while ((at = text.IndexOf(marker, at, StringComparison.Ordinal)) >= 0)
            {
                int start = text.IndexOf('{', at + marker.Length);
                if (start < 0) return null;
                string between = text.Substring(at + marker.Length, start - at - marker.Length);
                if (between.Trim().TrimStart('=', ':').Trim().Length > 0) { at += marker.Length; continue; }
                int depth = 0;
                bool inString = false, escaped = false;
                for (int i = start; i < text.Length; i++)
                {
                    char c = text[i];
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (c == '\\') escaped = true;
                        else if (c == '"') inString = false;
                        continue;
                    }
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}' && --depth == 0) return Json.TryParse(text.Substring(start, i - start + 1));
                }
                return null;
            }
            return null;
        }

        public static Video Parse(string html)
        {
            object player = ObjectAfter(html, "ytInitialPlayerResponse");
            if (player == null) return null;
            Video v = FromPlayer(player);
            Match key = Regex.Match(html ?? "", "\"INNERTUBE_API_KEY\"\\s*:\\s*\"([A-Za-z0-9_-]{20,60})\"");
            if (key.Success) v.ApiKey = key.Groups[1].Value;
            return v;
        }

        public static Video FromPlayer(object player)
        {
            Video v = new Video();
            v.Status = Json.Str(player, "playabilityStatus", "status") ?? "";
            v.Reason = Json.Str(player, "playabilityStatus", "reason") ?? "";
            object d = Json.Get(player, "videoDetails");
            v.Id = Json.Str(d, "videoId") ?? "";
            v.Title = Json.Str(d, "title") ?? "";
            v.Channel = Json.Str(d, "author") ?? "";
            v.Description = Json.Str(d, "shortDescription") ?? "";
            int.TryParse(Json.Str(d, "lengthSeconds") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out v.Seconds);
            long.TryParse(Json.Str(d, "viewCount") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out v.Views);
            string date = Json.Str(player, "microformat", "playerMicroformatRenderer", "publishDate") ?? Json.Str(player, "microformat", "playerMicroformatRenderer", "uploadDate") ?? "";
            v.Published = date.Length >= 10 ? date.Substring(0, 10) : date;
            List<object> tracks = Json.List(player, "captions", "playerCaptionsTracklistRenderer", "captionTracks");
            if (tracks != null)
                foreach (object t in tracks)
                {
                    Track k = new Track();
                    k.Url = Json.Str(t, "baseUrl") ?? "";
                    if (k.Url.Length == 0) continue;
                    k.Lang = Json.Str(t, "languageCode") ?? "";
                    k.Name = Json.Str(t, "name", "simpleText") ?? "";
                    if (k.Name.Length == 0)
                    {
                        List<object> runs = Json.List(t, "name", "runs");
                        if (runs != null) foreach (object r in runs) k.Name += Json.Str(r, "text") ?? "";
                    }
                    k.Auto = Json.Str(t, "kind") == "asr";
                    v.Tracks.Add(k);
                }
            return v;
        }

        public static string PlayerBase = "https://www.youtube.com/youtubei/v1/player";

        public static Video AppPlayer(string id, string apiKey, CancelToken ct, out string error)
        {
            error = null;
            Dictionary<string, object> client = Json.O("clientName", "ANDROID", "clientVersion", "20.10.38", "androidSdkVersion", 30, "hl", "en", "gl", "US");
            string body = Json.Write(Json.O("context", Json.O("client", client), "videoId", id, "contentCheckOk", true, "racyCheckOk", true));
            Dictionary<string, string> h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            h["User-Agent"] = "com.google.android.youtube/20.10.38 (Linux; U; Android 11) gzip";
            h["Accept-Language"] = "en-US,en;q=0.8";
            HttpResult r = Http.PostJson(PlayerBase + "?prettyPrint=false" + (string.IsNullOrEmpty(apiKey) ? "" : "&key=" + Uri.EscapeDataString(apiKey)), h, body, 20000, ct);
            if (!r.Ok) { error = r.Error ?? "YouTube did not answer"; return null; }
            object player = r.Json;
            if (player == null) { error = "YouTube sent something MinGent could not read"; return null; }
            Video v = FromPlayer(player);
            if (v.Id.Length == 0) v.Id = id;
            else if (!string.Equals(v.Id, id, StringComparison.Ordinal)) { error = "YouTube answered with a different video"; return null; }
            return v;
        }

        public static Video Info(string id, CancelToken ct, out string error)
        {
            error = null;
            HttpResult r = Http.Get("https://www.youtube.com/watch?v=" + id + "&hl=en", Headers(), 20000, ct);
            if (!r.Ok || string.IsNullOrEmpty(r.Text)) { error = r.Error ?? "YouTube did not answer"; return null; }
            Video v = Parse(r.Text);
            if (v == null)
            {
                error = r.Text.IndexOf("consent.youtube.com", StringComparison.OrdinalIgnoreCase) >= 0 ? "YouTube showed its cookie choice page instead of the video" :
                        r.Text.IndexOf("unusual traffic", StringComparison.OrdinalIgnoreCase) >= 0 ? "YouTube asked to prove this is not a bot" : "the page had no video data";
                return null;
            }
            if (v.Id.Length == 0) v.Id = id;
            return v;
        }

        public static Track Pick(List<Track> tracks)
        {
            Track best = null;
            int bestScore = -1;
            foreach (Track t in tracks)
            {
                bool en = t.Lang == "en" || t.Lang.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
                int score = (t.Auto ? 0 : 2) + (en ? 1 : 0);
                if (score > bestScore) { best = t; bestScore = score; }
            }
            return best;
        }

        public static bool NeedsToken(Track t) { return t != null && Regex.IsMatch(t.Url ?? "", @"[?&]exp=xpe(&|$)"); }

        public static string Captions(Track t, CancelToken ct, out string error)
        {
            error = null;
            string url = Regex.Replace(t.Url, @"([?&])fmt=[^&]*&?", "$1").TrimEnd('&', '?');
            url += (url.IndexOf('?') >= 0 ? "&" : "?") + "fmt=json3";
            HttpResult r = Http.Get(url, Headers(), 20000, ct);
            if (!r.Ok) { error = r.Error ?? "the captions did not load"; return null; }
            List<KeyValuePair<int, string>> segs = Json3(r.Text);
            if (segs.Count == 0) { error = "YouTube sent empty captions to a plain download"; return null; }
            return Paragraphs(segs);
        }

        public static List<KeyValuePair<int, string>> Json3(string body)
        {
            List<KeyValuePair<int, string>> l = new List<KeyValuePair<int, string>>();
            List<object> events = Json.List(Json.TryParse(body ?? ""), "events");
            if (events == null) return l;
            foreach (object e in events)
            {
                List<object> segs = Json.List(e, "segs");
                if (segs == null) continue;
                StringBuilder sb = new StringBuilder();
                foreach (object s in segs) sb.Append(Json.Str(s, "utf8") ?? "");
                string text = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                if (text.Length == 0) continue;
                l.Add(new KeyValuePair<int, string>((int)Json.Long(e, "tStartMs"), text));
            }
            return l;
        }

        public static List<KeyValuePair<int, string>> Segments(object fromBrowser)
        {
            List<KeyValuePair<int, string>> l = new List<KeyValuePair<int, string>>();
            List<object> segs = Json.List(fromBrowser, "segments");
            if (segs == null) return l;
            foreach (object s in segs)
            {
                List<object> pair = s as List<object>;
                if (pair == null || pair.Count < 2) continue;
                string text = Regex.Replace(pair[1] as string ?? "", @"\s+", " ").Trim();
                if (text.Length == 0) continue;
                double ms = pair[0] is double ? (double)pair[0] : 0;
                l.Add(new KeyValuePair<int, string>((int)ms, text));
            }
            return l;
        }

        public static string Time(int ms)
        {
            int s = Math.Max(0, ms / 1000);
            int h = s / 3600, m = s / 60 % 60, sec = s % 60;
            return h > 0 ? h + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + sec.ToString("00", CultureInfo.InvariantCulture)
                         : m + ":" + sec.ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Paragraphs(List<KeyValuePair<int, string>> segs)
        {
            StringBuilder all = new StringBuilder(), para = new StringBuilder();
            int start = -1;
            foreach (KeyValuePair<int, string> s in segs)
            {
                if (start < 0) start = s.Key;
                if (para.Length > 0) para.Append(' ');
                para.Append(s.Value);
                if (s.Key - start >= 30000 || para.Length >= 600)
                {
                    all.Append('[').Append(Time(start)).Append("] ").Append(para).Append('\n');
                    para.Length = 0;
                    start = -1;
                }
            }
            if (para.Length > 0) all.Append('[').Append(Time(start < 0 ? 0 : start)).Append("] ").Append(para).Append('\n');
            return all.ToString();
        }

        public static List<string> Split(string transcript)
        {
            List<string> parts = new List<string>();
            StringBuilder cur = new StringBuilder();
            foreach (string line in (transcript ?? "").Split('\n'))
            {
                if (line.Length == 0) continue;
                if (cur.Length > 0 && cur.Length + line.Length + 1 > PartChars) { parts.Add(cur.ToString()); cur.Length = 0; }
                cur.Append(line).Append('\n');
            }
            if (cur.Length > 0) parts.Add(cur.ToString());
            return parts;
        }

        public static string Header(Video v)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("YouTube video: ").Append(v.Title.Length > 0 ? v.Title : v.Id).Append('\n');
            List<string> facts = new List<string>();
            if (v.Channel.Length > 0) facts.Add("Channel: " + v.Channel);
            if (v.Published.Length > 0) facts.Add("Published: " + v.Published);
            if (v.Seconds > 0) facts.Add("Length: " + Time(v.Seconds * 1000));
            if (v.Views > 0) facts.Add("Views: " + v.Views.ToString("N0", CultureInfo.InvariantCulture));
            if (facts.Count > 0) sb.Append(string.Join("  |  ", facts.ToArray())).Append('\n');
            sb.Append("Address: https://www.youtube.com/watch?v=").Append(v.Id).Append('\n');
            string d = v.Description.Trim();
            if (d.Length > 0) sb.Append("\nDescription:\n").Append(d.Length > 2500 ? d.Substring(0, 2500) + "..." : d).Append('\n');
            return sb.ToString();
        }
    }
}
