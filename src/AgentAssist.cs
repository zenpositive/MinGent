using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MinGent
{
    static class Fuzzy
    {
        public static int Distance(string a, string b, int cap)
        {
            a = a ?? "";
            b = b ?? "";
            if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
            if (a.Length > 400 || b.Length > 400) return a == b ? 0 : cap + 1;
            int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int c = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + c);
                    if (cur[j] < rowMin) rowMin = cur[j];
                }
                if (rowMin > cap) return cap + 1;
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        static readonly Regex Token = new Regex(@"\w+|[^\w\s]", RegexOptions.CultureInvariant);

        public static HashSet<string> Tokens(string line)
        {
            HashSet<string> s = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Token.Matches(line ?? "")) s.Add(m.Value);
            return s;
        }

        public static double Similarity(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 && b.Count == 0) return 1;
            if (a.Count == 0 || b.Count == 0) return 0;
            int inter = 0;
            foreach (string t in a) if (b.Contains(t)) inter++;
            return inter / (double)(a.Count + b.Count - inter);
        }
    }

    static class ToolAliases
    {
        static readonly Dictionary<string, string[]> names = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static readonly Dictionary<string, Dictionary<string, object>> implied = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        static ToolAliases()
        {
            Add("read_file", "read", "view", "cat", "open_file", "file_read", "view_file", "get_file", "get_file_contents", "get_file_content", "read_text_file", "show_file", "load_file", "fs_read", "read_file_content", "read_document", "read_text", "view_source", "read_code", "read_lines", "read_files");
            Add("write_file", "write", "create_file", "create", "file_write", "save_file", "write_to_file", "new_file", "put_file", "fs_write", "create_new_file", "write_document", "save", "write_text", "overwrite_file", "write_code", "make_file", "append_file", "append_to_file");
            Add("edit_file", "edit", "str_replace", "replace_in_file", "search_replace", "search_and_replace", "apply_edit", "modify_file", "update_file", "file_edit", "replace", "edit_text", "replace_text", "change_file", "multi_edit", "edit_code", "apply_changes", "code_edit", "edit_document", "find_and_replace", "find_replace", "insert_text", "insert_code");
            Add("delete_file", "delete", "remove", "rm", "del", "remove_file", "delete_files", "remove_files", "delete_folder", "remove_folder", "delete_directory", "remove_directory", "rmdir", "trash", "move_to_trash", "recycle", "unlink", "fs_delete", "delete_path", "file_delete");
            Add("list_directory", "ls", "list", "list_dir", "list_files", "listdir", "dir", "list_folder", "read_dir", "read_directory", "list_directory_contents", "get_directory", "directory_list", "list_contents", "show_directory", "tree", "list_folder_contents", "view_directory", "folder_contents", "get_files");
            Add("find_files", "glob", "find", "find_file", "search_files", "file_search", "find_by_name", "locate", "find_path", "search_file", "list_files_recursive", "glob_files", "find_files_by_name", "search_file_names", "find_filenames");
            Add("search_text", "grep", "rg", "ripgrep", "search_code", "grep_search", "code_search", "find_in_files", "search_in_files", "search_file_content", "text_search", "codebase_search", "search_for_pattern", "search_content", "search_files_content", "find_text", "grep_files", "search_in_file", "search_codebase", "semantic_search");
            Add("run_command", "bash", "shell", "sh", "execute", "exec", "run", "terminal", "cmd", "command", "powershell", "pwsh", "run_terminal_cmd", "run_terminal_command", "execute_command", "run_shell_command", "shell_exec", "run_in_terminal", "execute_bash", "run_script", "system", "run_bash", "run_powershell", "exec_command", "terminal_command", "cli", "run_cmd", "execute_shell", "shell_command", "execute_powershell", "run_shell", "execute_terminal_command", "launch_process", "start_process", "run_program");
            Add("command_status", "bash_output", "command_output", "check_command", "get_command_output", "job_status", "process_status", "check_process", "read_output", "get_output", "check_status", "command_result", "background_status", "get_terminal_output", "read_process_output", "check_background");
            AddImplied("command_status", Json.O("stop", true), "kill_shell", "kill_command", "stop_command", "kill_bash", "terminate_command", "stop_process", "kill_process", "kill_job", "stop_job", "cancel_command", "terminate_process", "stop_server");
            Add("web_search", "search_web", "websearch", "google", "google_search", "internet_search", "bing_search", "duckduckgo", "duckduckgo_search", "brave_search", "search_internet", "web", "search_online", "online_search", "tavily_search", "internet");
            Add("web_fetch", "fetch", "fetch_url", "read_url", "http_get", "get_url", "url_fetch", "browse_url", "read_webpage", "fetch_webpage", "get_webpage", "scrape", "fetch_page", "read_web_page", "get_web_page", "download_page", "scrape_url", "fetch_content", "get_page");
            AddMany(new string[] { "browser_navigate", "web_fetch" }, null, "open_url", "open_link", "open_website");
            Add("todo_write", "todo", "todos", "update_todos", "write_todos", "plan", "update_plan", "set_todos", "task_list", "checklist", "manage_todo_list", "todo_list", "update_todo_list", "update_checklist", "write_plan", "tasks", "todo_update", "set_plan", "make_plan", "create_plan");
            Add("open_in_editor", "open_in_tab", "show_in_editor", "open_editor", "open_tab", "reveal_file", "show", "open_in_mingent", "open_in_justpad", "display_file");
            Add("browser_navigate", "navigate", "goto", "go_to", "browser_open", "browser_goto", "browse", "visit", "browser_navigate_to", "open_page", "open_browser", "navigate_to", "browser_go", "load_url", "visit_url", "go_to_url", "browser_visit", "browser_load");
            AddImplied("browser_navigate", Json.O("url", "back"), "go_back", "browser_back", "back", "navigate_back", "browser_go_back");
            AddImplied("browser_navigate", Json.O("url", "forward"), "go_forward", "browser_forward", "forward", "navigate_forward");
            AddImplied("browser_navigate", Json.O("url", "reload"), "reload", "refresh", "browser_reload", "browser_refresh", "reload_page", "refresh_page");
            AddImplied("close_pane", Json.O("pane", "browser"), "browser_close", "close_browser", "close_tab", "browser_close_tab", "close_page", "hide_browser", "browser_hide", "close_browser_pane", "browser_close_pane");
            AddImplied("open_in_editor", Json.O(), "open_notes", "show_notes", "open_notepad", "show_notepad", "open_notes_pane", "show_notes_pane", "notes_open", "notepad_open");
            AddImplied("close_pane", Json.O("pane", "notes"), "close_notes", "hide_notes", "close_notepad", "close_editor", "hide_editor", "close_notes_pane", "notes_close", "editor_close");
            Add("close_pane", "close_panel", "hide_pane", "close_panes");
            Add("browser_read", "read_page", "browser_get_text", "get_page_text", "page_text", "browser_snapshot", "snapshot", "browser_view", "browser_get_content", "get_page_content", "browser_content", "read_browser", "page_content", "browser_text", "get_text", "browser_get_page", "browser_read_page", "accessibility_snapshot");
            AddMany(new string[] { "browser_click", "app_click" }, null, "click", "click_element", "tap", "press_button", "click_button", "click_link", "left_click", "mouse_click");
            Add("browser_click", "browser_click_element", "browser_tap");
            AddMany(new string[] { "browser_type", "app_type" }, null, "type", "type_text", "fill", "input", "input_text", "enter_text", "fill_field", "fill_input", "set_value", "form_input");
            Add("browser_type", "browser_fill", "browser_input", "browser_fill_form", "browser_type_text");
            AddMany(new string[] { "browser_press_key", "app_key" }, null, "press_key", "press", "key", "keypress", "send_key", "keyboard", "key_press", "send_keys", "hotkey", "shortcut", "keyboard_shortcut", "hold_key", "hold_keys", "key_hold", "keydown", "key_down", "press_and_hold", "hold_down");
            Add("browser_press_key", "browser_key", "browser_press", "browser_keypress", "browser_send_key", "browser_hold_key", "browser_key_down");
            Add("browser_scroll", "scroll", "scroll_page", "page_scroll", "browser_scroll_page", "mouse_wheel", "wheel", "scroll_element");
            AddMany(new string[] { "browser_hover", "app_hover" }, null, "hover", "mouse_move", "move_mouse", "mouse_over", "mouseover", "hover_element", "move_to", "mouse_hover", "point_at");
            Add("browser_hover", "browser_mouse_move", "browser_move_mouse", "browser_mouseover");
            AddMany(new string[] { "browser_drag", "app_drag" }, null, "drag", "drag_and_drop", "drag_drop", "dragdrop", "drag_to", "mouse_drag", "left_click_drag", "drag_element", "swipe");
            Add("browser_drag", "browser_drag_and_drop", "browser_drag_drop", "browser_swipe");
            AddImplied("browser_click", Json.O("button", "right"), "browser_right_click", "browser_context_click");
            AddImplied("browser_click", Json.O("double", true), "browser_double_click", "browser_dblclick");
            AddImplied("browser_click", Json.O("button", "middle"), "browser_middle_click", "middle_click");
            AddImplied("browser_click", Json.O("clicks", 3.0), "browser_triple_click", "triple_click");
            AddImplied("browser_scroll", Json.O("direction", "down"), "scroll_down", "browser_scroll_down", "page_down");
            AddImplied("browser_scroll", Json.O("direction", "up"), "scroll_up", "browser_scroll_up", "page_up");
            AddImplied("browser_scroll", Json.O("direction", "left"), "scroll_left", "browser_scroll_left");
            AddImplied("browser_scroll", Json.O("direction", "right"), "scroll_right", "browser_scroll_right");
            AddMany(new string[] { "browser_screenshot", "app_screenshot" }, null, "screenshot", "take_screenshot", "capture_screenshot", "capture_screen", "screen_capture", "screen_shot");
            Add("browser_screenshot", "browser_take_screenshot", "capture_page", "page_screenshot", "browser_capture");
            Add("browser_console", "console", "read_console", "browser_read_console", "console_messages", "browser_console_messages", "console_logs", "browser_console_logs", "get_console_logs", "get_console_messages",
                "read_console_messages", "browser_logs", "page_console", "devtools_console", "browser_devtools", "devtools", "browser_evaluate", "evaluate", "browser_eval", "eval", "evaluate_script", "execute_script",
                "browser_execute_script", "execute_javascript", "run_javascript", "javascript", "js", "run_js", "eval_js", "evaluate_js", "browser_javascript", "browser_js", "page_evaluate", "javascript_tool",
                "browser_run_javascript", "execute_js", "browser_run_js");
            AddImplied("memory", Json.O("action", "add"), "remember", "save_memory", "add_memory", "memorize", "store_memory", "save_note", "update_memory", "write_memory", "memory_add", "remember_fact", "save_fact", "store_fact", "memory_save");
            AddImplied("memory", Json.O("action", "list"), "recall", "list_memory", "read_memory", "get_memory", "view_memory", "memories", "list_memories", "memory_list", "memory_read", "show_memory");
            AddImplied("memory", Json.O("action", "remove"), "forget", "remove_memory", "delete_memory", "memory_remove", "memory_delete", "forget_fact");
            Add("analyze_audio", "audio_analysis", "analyse_audio", "analyze_sound", "analyse_sound", "audio_analyze", "audio_info", "audio_stats", "inspect_audio", "read_audio", "listen", "listen_audio", "listen_to_audio", "hear_audio", "audio_analyzer", "analyze_music", "analyze_song", "analyze_wav", "analyze_mp3", "waveform", "spectrogram", "get_audio_info", "audio_metadata", "measure_audio", "loudness", "detect_beats", "find_beats", "beat_track", "beat_tracking", "detect_tempo", "detect_onsets", "align_audio", "sync_audio", "compare_audio");
            Add("browser_upload", "upload_file", "file_upload", "browser_file_upload", "browser_upload_file", "upload", "attach_file", "browser_attach_file", "set_input_files", "browser_set_files", "choose_file", "browser_choose_file");
            Add("watch_video", "view_video", "see_video", "video_frames", "video_frame", "get_video_frames", "extract_frames", "video_screenshot", "video_screenshots", "watch", "analyze_video", "analyse_video", "look_at_video", "inspect_video", "browser_watch_video");
            Add("system_info", "sysinfo", "system_information", "get_system_info", "os_info", "environment_info", "machine_info", "get_environment", "env_info", "computer_info", "hardware_info", "pc_info", "get_os_info", "system_details");
            Add("app_read", "read_app", "read_window", "window_read", "desktop_read", "ui_read", "get_window", "list_windows", "inspect_window", "app_snapshot", "window_snapshot", "get_ui", "app_inspect", "read_ui", "get_window_elements", "app_elements");
            Add("app_screenshot", "window_screenshot", "desktop_screenshot", "app_capture", "screenshot_window", "capture_window", "app_screen", "screenshot_app");
            Add("app_click", "desktop_click", "window_click", "click_app", "app_tap", "click_in_app");
            AddImplied("app_click", Json.O("right", true), "right_click", "app_right_click", "context_click");
            AddImplied("app_click", Json.O("double", true), "double_click", "app_double_click", "dbl_click", "doubleclick");
            Add("app_type", "desktop_type", "type_in_app", "window_type", "app_input", "type_into_app", "app_type_text");
            Add("app_key", "desktop_key", "window_key", "app_press_key", "app_hotkey", "app_shortcut", "app_keypress", "app_hold_key", "app_key_down");
            Add("app_scroll", "desktop_scroll", "window_scroll", "app_wheel", "scroll_app", "app_mouse_wheel");
            Add("app_hover", "desktop_hover", "app_mouse_move", "app_move_mouse", "window_hover");
            Add("app_drag", "desktop_drag", "app_drag_and_drop", "window_drag", "app_swipe");
            Add("delegate", "task", "agent", "subagent", "sub_agent", "spawn_agent", "dispatch_agent", "run_agent", "delegate_task", "ask_helper", "helper",
                "launch_agent", "create_agent", "run_subagent", "call_agent", "start_agent", "spawn_subagent", "new_agent", "use_agent", "run_helper", "start_helper", "invoke_agent", "agent_task");
            Add("apply_patch", "patch", "apply_diff", "diff", "apply_unified_diff", "patch_file", "apply_patches", "unified_diff");
            Add("ask_user", "ask", "ask_question", "ask_user_question", "askuserquestion", "ask_followup_question", "ask_follow_up_question", "question", "clarify", "ask_clarification",
                "request_clarification", "request_user_input", "user_input", "get_user_input", "prompt_user", "ask_human", "human_input", "ask_the_user", "wait_for_user", "ask_for_input", "ask_user_input");
            Add("present_plan", "exit_plan_mode", "exitplanmode", "submit_plan", "propose_plan", "show_plan", "present", "approve_plan", "request_approval", "finish_plan", "plan_ready", "final_plan", "share_plan");
        }

        static void Add(string target, params string[] aliases) { AddMany(new string[] { target }, null, aliases); }
        static void AddImplied(string target, Dictionary<string, object> args, params string[] aliases) { AddMany(new string[] { target }, args, aliases); }

        static void AddMany(string[] targets, Dictionary<string, object> args, params string[] aliases)
        {
            foreach (string a in aliases)
            {
                string n = Norm(a);
                foreach (string k in new string[] { n, n.Replace("_", "") })
                {
                    if (k.Length == 0 || names.ContainsKey(k)) continue;
                    names[k] = targets;
                    if (args != null) implied[k] = args;
                }
            }
        }

        public static string Norm(string s)
        {
            string t = (s ?? "").Trim();
            StringBuilder sb = new StringBuilder(t.Length + 4);
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && (char.IsLower(t[i - 1]) || char.IsDigit(t[i - 1])) && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        public static string Resolve(string name, ICollection<string> known, Dictionary<string, object> args, bool preferApp, out Dictionary<string, object> impliedArgs)
        {
            impliedArgs = null;
            if (string.IsNullOrEmpty(name) || known == null) return null;
            if (known.Contains(name)) return name;
            string nm = name.Trim().Trim('"', '\'', '`');
            List<string> cands = new List<string>();
            cands.Add(nm);
            foreach (string sep in new string[] { "__", ".", ":", "/", "\\" })
            {
                int i = nm.LastIndexOf(sep, StringComparison.Ordinal);
                if (i >= 0 && i + sep.Length < nm.Length) cands.Add(nm.Substring(i + sep.Length));
            }
            foreach (string c in cands)
            {
                string n = Norm(c);
                if (n.Length == 0) continue;
                foreach (string k in known) if (Norm(k) == n) return k;
                string flat = n.Replace("_", "");
                foreach (string k in known) if (Norm(k).Replace("_", "") == flat) return k;
                string special = Special(n, known, args, out impliedArgs);
                if (special != null) return special;
                foreach (string key in new string[] { n, flat })
                {
                    string[] targets;
                    if (!names.TryGetValue(key, out targets)) continue;
                    string pick = null;
                    if (preferApp) foreach (string t in targets) if (pick == null && t.StartsWith("app_", StringComparison.Ordinal) && known.Contains(t)) pick = t;
                    foreach (string t in targets) if (pick == null && known.Contains(t)) pick = t;
                    if (pick == null) continue;
                    Dictionary<string, object> imp;
                    if (implied.TryGetValue(key, out imp)) impliedArgs = imp;
                    return pick;
                }
            }
            return null;
        }

        static string Special(string n, ICollection<string> known, Dictionary<string, object> args, out Dictionary<string, object> impliedArgs)
        {
            impliedArgs = null;
            if (n == "str_replace_editor" || n == "str_replace_based_edit_tool" || n == "text_editor" || n == "editor" || n == "file_editor" ||
                n.StartsWith("text_editor_", StringComparison.Ordinal) || n.StartsWith("str_replace_based_edit_tool", StringComparison.Ordinal))
            {
                string cmd = (Json.Str(args, "command") ?? "").Trim().ToLowerInvariant();
                if (cmd == "view") return Known(known, "read_file");
                if (cmd == "create") return Known(known, "write_file");
                if (cmd == "str_replace" || cmd == "insert") return Known(known, "edit_file");
                if (cmd == "undo_edit")
                {
                    impliedArgs = Json.O("__unsupported", "undo_edit is not available. To revert a change, edit the text back with edit_file.");
                    return Known(known, "edit_file");
                }
                if (args != null && args.ContainsKey("file_text")) return Known(known, "write_file");
                if (args != null && (args.ContainsKey("old_str") || args.ContainsKey("new_str"))) return Known(known, "edit_file");
                return Known(known, "read_file");
            }
            if (n == "search" || n == "find_anything" || n == "lookup")
            {
                bool code = HasKey(args, "path", "dir", "directory", "folder", "include", "file_pattern", "glob", "regex", "file", "files", "type", "pattern", "case_sensitive");
                return code ? (Known(known, "search_text") ?? Known(known, "web_search")) : (Known(known, "web_search") ?? Known(known, "search_text"));
            }
            if (n == "open")
            {
                string u = null;
                if (args != null) foreach (KeyValuePair<string, object> kv in args) { string s = kv.Value as string; if (s != null) { u = s; break; } }
                if (u != null && Regex.IsMatch(u.Trim(), @"^(https?://|www\.)", RegexOptions.IgnoreCase)) return Known(known, "browser_navigate") ?? Known(known, "web_fetch");
                return Known(known, "read_file");
            }
            return null;
        }

        static bool HasKey(Dictionary<string, object> args, params string[] keys)
        {
            if (args == null) return false;
            foreach (string k in args.Keys)
            {
                string nk = Norm(k);
                foreach (string w in keys) if (nk == w) return true;
            }
            return false;
        }

        static string Known(ICollection<string> known, string n) { return known.Contains(n) ? n : null; }

        public static string Suggest(string name, ICollection<string> known)
        {
            string n = Norm(name);
            string best = null;
            int bd = int.MaxValue;
            foreach (string k in known)
            {
                int d = Fuzzy.Distance(n, Norm(k), 6);
                if (d < bd) { bd = d; best = k; }
            }
            return bd <= Math.Max(2, n.Length / 3) ? best : null;
        }

        public static List<object> RawAfterExpand(List<object> raw, string provider, List<ToolCall> calls)
        {
            if (provider != "anthropic") return provider == "openai-responses" ? null : raw;
            List<object> res = new List<object>();
            foreach (object b in raw) if (Json.Str(b, "type") != "tool_use") res.Add(b);
            foreach (ToolCall c in calls) res.Add(Json.O("type", "tool_use", "id", c.Id, "name", c.Name, "input", (object)c.Args ?? new Dictionary<string, object>()));
            return res;
        }

        public static bool ExpandParallel(List<ToolCall> calls)
        {
            bool changed = false;
            for (int i = 0; i < calls.Count; i++)
            {
                ToolCall c = calls[i];
                string n = Norm(c.Name);
                if (n != "multi_tool_use_parallel" && n != "parallel" && n != "multi_tool_use" && n != "parallel_tool_calls" && n != "multi_tool") continue;
                Dictionary<string, object> a = c.Args;
                List<object> uses = Json.List(a, "tool_uses") ?? Json.List(a, "calls") ?? Json.List(a, "tool_calls") ?? Json.List(a, "tools");
                if (uses == null || uses.Count == 0) continue;
                List<ToolCall> repl = new List<ToolCall>();
                int k = 0;
                foreach (object u in uses)
                {
                    string nm = Json.Str(u, "recipient_name") ?? Json.Str(u, "name") ?? Json.Str(u, "tool") ?? Json.Str(u, "tool_name") ?? Json.Str(u, "function", "name");
                    if (string.IsNullOrEmpty(nm)) continue;
                    object p = Json.Get(u, "parameters") ?? Json.Get(u, "arguments") ?? Json.Get(u, "args") ?? Json.Get(u, "input") ?? Json.Get(u, "function", "arguments");
                    ToolCall nc = new ToolCall();
                    nc.Id = (string.IsNullOrEmpty(c.Id) ? "call" : c.Id) + "_" + (k++).ToString(CultureInfo.InvariantCulture);
                    nc.Name = nm;
                    nc.Arguments = p is string ? (string)p : Json.Write(p ?? new Dictionary<string, object>());
                    repl.Add(nc);
                }
                if (repl.Count == 0) continue;
                calls.RemoveAt(i);
                calls.InsertRange(i, repl);
                i += repl.Count - 1;
                changed = true;
            }
            return changed;
        }
    }

    static class ArgFixer
    {
        static readonly Dictionary<string, string[]> aliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static readonly HashSet<string> wrappers = new HashSet<string>(StringComparer.Ordinal) { "args", "arguments", "input", "parameters", "params", "kwargs", "tool_input", "function_args", "payload", "data" };

        static ArgFixer()
        {
            A("path", "file_path", "filepath", "file", "filename", "file_name", "target_file", "absolute_path", "relative_path", "uri", "target", "notebook_path", "source_file", "target_path", "path_to_file", "file_location", "fname", "dir", "directory", "folder", "target_directory", "dir_path", "directory_path", "folder_path", "root", "base_path", "relative_workspace_path", "search_path", "location", "tab", "document", "dirname", "folder_name", "directory_name", "pathname", "cwd");
            A("content", "contents", "text", "data", "file_text", "body", "code", "file_content", "new_content", "source", "value", "file_contents", "content_text", "string", "full_content", "new_file_content");
            A("old_string", "old_str", "old", "old_text", "search", "find", "original", "original_text", "from", "target_text", "search_string", "before", "old_code", "to_replace", "search_text", "find_text", "existing", "existing_text", "current", "current_text", "original_code", "text_to_replace", "old_content", "oldtext", "search_for", "match", "old_value", "replace_from");
            A("new_string", "new_str", "new", "new_text", "replace", "replacement", "to", "with", "after", "new_code", "replace_with", "replacement_text", "updated", "updated_text", "newtext", "replace_text", "new_value", "replace_to", "substitute", "updated_code");
            A("replace_all", "all", "global", "replace_all_occurrences", "every", "replace_every", "all_occurrences", "global_replace", "replace_globally");
            A("offset", "start_line", "line_start", "start", "from_line", "line", "begin", "first_line", "line_number", "start_line_number", "startline", "line_offset", "offset_line", "from", "start_at", "start_index");
            A("limit", "lines", "count", "max_lines", "num_lines", "line_count", "n", "length", "max", "number_of_lines", "nlines", "maxlines", "max_count", "lines_to_read", "num");
            A("command", "cmd", "script", "code", "command_line", "commandline", "input", "shell_command", "cmdline", "run", "exec", "bash_command", "powershell_command", "cmd_line", "statement", "commands", "bash", "powershell", "shell_cmd", "ps", "cmd_string", "command_text");
            A("cwd", "workdir", "working_directory", "working_dir", "directory", "dir", "path", "cd", "folder", "cwd_path", "current_dir", "current_directory", "work_dir", "in_dir", "location", "root", "run_in", "workspace");
            A("shell", "shell_type", "interpreter", "executor", "terminal");
            A("timeout_seconds", "timeout", "timeout_sec", "timeout_s", "time_limit", "timeout_secs", "max_seconds", "timeout_in_seconds", "max_time", "time_out");
            A("background", "run_in_background", "is_background", "async", "detach", "detached", "in_background", "bg", "nowait", "daemon", "no_wait", "run_async", "background_process", "run_background", "is_async");
            A("id", "command_id", "job_id", "bash_id", "shell_id", "process_id", "pid", "task_id", "cmd_id", "background_id", "handle", "session_id", "terminal_id", "proc_id");
            A("wait_seconds", "wait", "wait_time", "block_seconds", "wait_secs", "timeout", "seconds", "wait_for", "wait_sec", "delay");
            A("stop", "kill", "terminate", "cancel", "abort", "stop_command", "kill_command", "end");
            A("url", "uri", "link", "address", "page", "href", "website", "site", "target_url", "webpage", "web_address", "page_url", "location", "path", "file", "file_path", "src");
            A("query", "q", "search", "search_query", "text", "keywords", "term", "question", "prompt", "search_term", "query_string", "input", "topic", "pattern", "regex", "keyword", "string", "needle", "regexp", "expression", "find", "grep", "search_string", "search_pattern", "search_text", "phrase", "words");
            A("pattern", "glob", "glob_pattern", "name", "file_pattern", "query", "search", "filename", "mask", "wildcard", "file_name", "files", "file_glob", "include", "name_pattern", "filter", "file_mask", "regex", "search_pattern", "file", "target");
            A("file_pattern", "include", "glob", "files", "file_glob", "include_pattern", "file_filter", "filter", "extensions", "file_mask", "name_pattern", "glob_pattern", "includes", "file_types", "files_pattern", "only");
            A("regex", "is_regex", "use_regex", "regexp_mode", "regex_mode", "is_regexp", "use_regexp", "regular_expression");
            A("case_sensitive", "match_case", "case", "casesensitive", "case_sensitive_search", "respect_case");
            A("todos", "items", "tasks", "list", "plan", "todo_list", "todolist", "todo", "steps", "checklist", "entries", "task_list", "todo_items");
            A("plan", "content", "text", "markdown", "plan_text", "plan_markdown", "body", "steps", "proposal", "description", "details", "summary", "message");
            A("ref", "element", "element_ref", "id", "target", "element_id", "ref_id", "reference", "element_index", "index", "node", "node_id", "selector", "ref_number", "element_reference", "elem", "ref_name", "control", "control_id");
            A("text", "value", "input", "content", "string", "keys", "query", "input_text", "characters", "message", "data", "type_text", "chars");
            A("submit", "enter", "press_enter", "submit_form", "hit_enter", "and_enter", "then_enter", "send");
            A("clear", "clear_first", "replace", "overwrite", "clear_field", "clear_existing");
            A("key", "keys", "key_name", "keycode", "combo", "shortcut", "hotkey", "key_combo", "combination", "button", "keystroke", "key_combination", "name");
            A("direction", "dir", "way", "scroll_direction", "to", "scroll");
            A("line", "line_number", "lineno", "line_num", "at_line", "goto_line", "row", "start_line");
            A("fact", "text", "content", "memory", "note", "value", "info", "data", "item", "entry", "fact_text", "statement", "knowledge", "information", "detail");
            A("action", "op", "operation", "mode", "command", "type", "method", "verb", "act");
            A("window", "title", "window_title", "app", "application", "target_window", "window_name", "process", "program", "app_name", "window_id");
            A("double", "double_click", "doubleclick", "dbl", "dblclick", "is_double");
            A("button", "mouse_button", "which_button", "btn", "click_button");
            A("clicks", "click_count", "num_clicks", "count_clicks", "times_clicked");
            A("modifiers", "modifier", "mods", "hold_keys", "with_keys", "keys_held", "held_keys", "modifier_keys");
            A("hold_ms", "hold", "duration", "duration_ms", "press_ms", "hold_time", "long_press_ms", "hold_duration");
            A("repeat", "times", "count", "presses", "repetitions", "n_times", "repeat_count");
            A("pixels", "amount", "distance", "delta", "dy", "dx", "scroll_amount", "px", "by");
            A("part", "page", "chunk", "part_number", "page_number", "next_part", "section_number");
            A("recent", "recency", "time_range", "timelimit", "time_limit", "freshness", "date_range", "time_filter", "period", "since", "timeframe", "published_within");
            A("notches", "amount", "steps", "ticks", "wheel_clicks", "scroll_amount", "lines", "times", "count", "repeat", "distance", "delta", "clicks", "scroll_clicks");
            A("to_ref", "target_ref", "drop_ref", "destination_ref", "end_ref", "to_element", "drop_target", "target_element", "dest_ref");
            A("to_x", "end_x", "x2", "target_x", "dest_x", "drop_x");
            A("to_y", "end_y", "y2", "target_y", "dest_y", "drop_y");
            A("dx", "delta_x", "x_delta", "rel_x", "relative_x", "by_x");
            A("dy", "delta_y", "y_delta", "rel_y", "relative_y", "by_y");
            A("right", "right_click", "rightclick", "context_click", "is_right", "right_button");
            A("input", "patch", "diff", "patch_text", "content", "text", "changes", "unified_diff");
            A("append", "append_mode", "add_to_end", "mode_append");
            A("task", "prompt", "instructions", "instruction", "description", "query", "goal", "objective", "message", "content", "details", "request", "text", "assignment", "job", "mission", "work");
            A("kind", "subagent_type", "agent_type", "type", "mode", "role", "agent", "category", "helper_type", "task_type", "agent_kind");
            A("title", "description", "name", "label", "summary", "short_description", "subject", "headline");
            A("question", "prompt", "query", "text", "message", "ask", "q", "question_text", "header", "title", "content");
            A("options", "choices", "answers", "suggestions", "buttons", "option_list", "possible_answers", "answer_options", "items", "alternatives", "selections");
            A("details", "context", "description", "explanation", "info", "detail", "body", "note", "reason");
            A("description", "summary", "explanation", "purpose", "desc", "what", "what_it_does", "intent", "comment");
            A("code", "script", "expression", "js", "javascript", "js_code", "javascript_code", "source", "source_code", "snippet", "function", "func", "eval", "expr", "statement", "statements", "command", "program", "input", "text", "body");
        }

        static void A(string param, params string[] list)
        {
            string[] norm = new string[list.Length];
            for (int i = 0; i < list.Length; i++) norm[i] = ToolAliases.Norm(list[i]);
            aliases[param] = norm;
        }

        public static string Fix(AgentTool tool, ToolCall call)
        {
            Dictionary<string, object> schema = tool.Schema;
            Dictionary<string, object> props = Json.Obj(schema, "properties") ?? new Dictionary<string, object>();
            bool changed = false;
            Dictionary<string, object> src = call.Args;
            if (src == null)
            {
                string raw = (call.Arguments ?? "").Trim();
                if (raw.Length == 0 || raw[0] == '{' || raw[0] == '[') return null;
                string val = raw;
                if (raw[0] == '"')
                {
                    val = Json.TryParse(raw) as string;
                    if (val == null) return null;
                }
                string primary = Primary(schema, props);
                if (primary == null) return null;
                src = new Dictionary<string, object>();
                src[primary] = val;
                changed = true;
            }
            Dictionary<string, object> a = new Dictionary<string, object>(src);
            for (int round = 0; round < 2; round++)
            {
                if (a.Count == 1)
                {
                    foreach (KeyValuePair<string, object> kv in new List<KeyValuePair<string, object>>(a))
                    {
                        if (props.ContainsKey(kv.Key) || !wrappers.Contains(ToolAliases.Norm(kv.Key))) continue;
                        Dictionary<string, object> inner = kv.Value as Dictionary<string, object>;
                        string sv = kv.Value as string;
                        if (inner == null && sv != null && sv.TrimStart().StartsWith("{")) inner = Json.TryParse(sv) as Dictionary<string, object>;
                        if (inner != null) { a = new Dictionary<string, object>(inner); changed = true; }
                    }
                }
                if (a.ContainsKey("name") && !props.ContainsKey("name") && (a.ContainsKey("arguments") || a.ContainsKey("parameters")))
                {
                    object inner = a.ContainsKey("arguments") ? a["arguments"] : a["parameters"];
                    Dictionary<string, object> d = inner as Dictionary<string, object>;
                    if (d == null && inner is string) d = Json.TryParse((string)inner) as Dictionary<string, object>;
                    if (d != null) { a = new Dictionary<string, object>(d); changed = true; }
                }
            }
            if (call.Implied != null)
                foreach (KeyValuePair<string, object> kv in call.Implied)
                    if (!a.ContainsKey(kv.Key)) { a[kv.Key] = kv.Value; changed = true; }
            object unsupported;
            if (a.TryGetValue("__unsupported", out unsupported) && unsupported is string) return (string)unsupported;
            PreFix(tool.Name, a, ref changed);

            Dictionary<string, string> normToKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string k in new List<string>(a.Keys))
            {
                if (props.ContainsKey(k)) continue;
                string nk = ToolAliases.Norm(k);
                if (nk != k && props.ContainsKey(nk) && !a.ContainsKey(nk)) { a[nk] = a[k]; a.Remove(k); changed = true; continue; }
                if (!normToKey.ContainsKey(nk)) normToKey[nk] = k;
                string flat = nk.Replace("_", "");
                if (!normToKey.ContainsKey(flat)) normToKey[flat] = k;
            }
            Dictionary<string, string> usedAlias = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string p in new List<string>(props.Keys))
            {
                if (a.ContainsKey(p)) continue;
                string[] list;
                if (!aliases.TryGetValue(p, out list)) continue;
                foreach (string al in list)
                {
                    if (props.ContainsKey(al)) continue;
                    string key;
                    if (!normToKey.TryGetValue(al, out key) && !normToKey.TryGetValue(al.Replace("_", ""), out key)) continue;
                    if (!a.ContainsKey(key)) continue;
                    a[p] = a[key];
                    a.Remove(key);
                    usedAlias[p] = al;
                    changed = true;
                    break;
                }
            }
            string err = Special(tool.Name, a, usedAlias, ref changed);
            if (err != null) return err;
            List<string> missing = Missing(tool.Name, schema, a);
            if (missing.Count == 1)
            {
                string only = null;
                int extra = 0;
                foreach (string k in a.Keys) if (!props.ContainsKey(k) && !k.StartsWith("__", StringComparison.Ordinal)) { extra++; only = k; }
                Dictionary<string, object> ps = Json.Obj(props, missing[0]);
                if (extra == 1 && Json.Str(ps, "type") == "string" && a[only] is string)
                {
                    a[missing[0]] = a[only];
                    a.Remove(only);
                    missing.Clear();
                    changed = true;
                }
            }
            if (changed) call.SetArgs(a);
            if (missing.Count > 0) return MissingMessage(tool, missing, src);
            return null;
        }

        static void Rename(Dictionary<string, object> a, string to, ref bool changed, params string[] from)
        {
            if (a.ContainsKey(to)) return;
            object v = Take(a, from);
            if (v != null) { a[to] = v; changed = true; }
        }

        static void PointInto(Dictionary<string, object> a, string xKey, string yKey, ref bool changed, params string[] from)
        {
            if (a.ContainsKey(xKey) && a.ContainsKey(yKey)) return;
            foreach (string f in from)
            {
                object v = null;
                foreach (string k in new List<string>(a.Keys)) if (ToolAliases.Norm(k) == f) { v = a[k]; break; }
                if (v == null) continue;
                double x, y;
                List<object> l = v as List<object>;
                if (l == null && v is string && ((string)v).TrimStart().StartsWith("[")) l = Json.TryParse((string)v) as List<object>;
                bool ok = false;
                if (l != null && l.Count >= 2 && TryNum(l[0], out x) && TryNum(l[1], out y)) { a[xKey] = x; a[yKey] = y; ok = true; }
                else if (v is Dictionary<string, object> && TryNum(Json.Get(v, "x"), out x) && TryNum(Json.Get(v, "y"), out y)) { a[xKey] = x; a[yKey] = y; ok = true; }
                else if (v is string)
                {
                    Match m = Regex.Match((string)v, @"^\s*\(?\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)\s*\)?\s*$");
                    if (m.Success) { a[xKey] = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); a[yKey] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); ok = true; }
                }
                if (ok) { Take(a, f); changed = true; return; }
            }
        }

        static void PreFix(string tool, Dictionary<string, object> a, ref bool changed)
        {
            if (tool == "browser_drag" || tool == "app_drag")
            {
                PointInto(a, "x", "y", ref changed, "start_coordinate", "start", "from", "from_coordinate", "source_coordinate", "start_position", "from_position", "start_point");
                PointInto(a, "to_x", "to_y", ref changed, "coordinate", "end_coordinate", "end", "to", "to_coordinate", "target_coordinate", "end_position", "to_position", "end_point", "destination");
                Rename(a, "ref", ref changed, "source", "from", "from_ref", "source_ref", "drag_ref", "start_ref", "element_from", "source_element", "from_element", "start");
                Rename(a, "to_ref", ref changed, "target", "to", "destination", "dest", "drop", "end", "drop_on", "onto");
            }
            else if (tool == "browser_click" || tool == "browser_hover" || tool == "browser_scroll" || tool == "app_click" || tool == "app_hover" || tool == "app_scroll")
                PointInto(a, "x", "y", ref changed, "coordinate", "coordinates", "position", "point", "pos", "xy", "at", "location", "coords");
            else if (tool == "app_screenshot")
                Rename(a, "zoom", ref changed, "area", "region", "box", "rect", "crop", "zoom_area", "zoom_region");
            else if (tool == "browser_upload")
            {
                Rename(a, "path", ref changed, "file", "file_path", "filepath", "filename", "files", "paths", "file_paths", "local_path");
                Rename(a, "ref", ref changed, "element", "selector", "target", "input", "file_input");
                Rename(a, "field", ref changed, "index", "number", "n", "which");
            }
            else if (tool == "watch_video")
            {
                Rename(a, "times", ref changed, "time", "timestamps", "timestamp", "moments", "moment", "at", "seconds", "positions");
                Rename(a, "from", ref changed, "start", "start_time", "begin", "from_time", "start_at");
                Rename(a, "to", ref changed, "end", "end_time", "until", "to_time", "end_at", "stop");
            }
            else if (tool == "analyze_audio")
            {
                Rename(a, "start", ref changed, "start_time", "from", "begin", "start_seconds", "start_sec", "offset", "from_seconds", "seek", "start_at", "t0");
                Rename(a, "end", ref changed, "end_time", "to", "stop", "until", "end_seconds", "end_sec", "to_seconds", "end_at", "t1");
                Rename(a, "pictures", ref changed, "images", "include_images", "plots", "show_images", "graphs", "charts", "visualize", "visualise", "draw", "image");
                Rename(a, "focus", ref changed, "mode", "analysis", "analysis_type", "type", "kind", "detail", "report", "view", "focus_on");
                Rename(a, "compare_with", ref changed, "compare", "compare_to", "other", "other_path", "other_file", "second", "second_path", "second_file", "reference", "reference_path", "align_with", "align_to", "sync_with", "sync_to", "against", "path2", "file2");
                object span = Take(a, "duration", "length", "seconds", "span", "duration_seconds");
                if (span != null && !a.ContainsKey("end"))
                {
                    object st;
                    a.TryGetValue("start", out st);
                    double d = AudioTime.Parse(span, -1);
                    if (d > 0) a["end"] = Math.Max(0, AudioTime.Parse(st, 0)) + d;
                    changed = true;
                }
            }
        }

        static string Primary(Dictionary<string, object> schema, Dictionary<string, object> props)
        {
            List<object> req = Json.List(schema, "required");
            if (req != null && req.Count == 1) return req[0] as string;
            foreach (string p in new string[] { "command", "path", "query", "url", "pattern", "input", "text", "key", "fact", "todos" })
                if (props.ContainsKey(p) && (req == null || req.Count == 0 || req.Contains(p))) return p;
            return null;
        }

        static List<string> Missing(string tool, Dictionary<string, object> schema, Dictionary<string, object> a)
        {
            List<string> l = new List<string>();
            List<object> req = Json.List(schema, "required");
            if (req == null) return l;
            bool multi = tool == "edit_file" && (a.ContainsKey("edits") || a.ContainsKey("insert_line"));
            object stopping;
            bool stops = tool == "delegate" && a.TryGetValue("stop", out stopping) && stopping != null && Convert.ToString(stopping, System.Globalization.CultureInfo.InvariantCulture).Trim().Length > 0;
            foreach (object o in req)
            {
                string p = o as string;
                if (p == null) continue;
                if (multi && (p == "old_string" || (p == "new_string" && a.ContainsKey("edits")))) continue;
                if (stops && p == "task") continue;
                object v;
                if (!a.TryGetValue(p, out v) || v == null) l.Add(p);
            }
            return l;
        }

        static string MissingMessage(AgentTool tool, List<string> missing, Dictionary<string, object> sent)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Missing required argument").Append(missing.Count > 1 ? "s " : " ");
            for (int i = 0; i < missing.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('"').Append(missing[i]).Append('"'); }
            sb.Append(". ").Append(tool.Name).Append(" takes: ").Append(Signature(tool)).Append('.');
            List<string> keys = new List<string>();
            if (sent != null) foreach (string k in sent.Keys) keys.Add(k);
            sb.Append(" You sent: ").Append(keys.Count == 0 ? "no arguments" : string.Join(", ", keys.ToArray())).Append('.');
            return sb.ToString();
        }

        public static string Signature(AgentTool tool)
        {
            Dictionary<string, object> props = Json.Obj(tool.Schema, "properties");
            if (props == null || props.Count == 0) return "no arguments";
            List<object> req = Json.List(tool.Schema, "required") ?? new List<object>();
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, object> kv in props)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(kv.Key).Append(" (").Append(Json.Str(kv.Value, "type") ?? "any").Append(req.Contains(kv.Key) ? ", required)" : ")");
                string d = Json.Str(kv.Value, "description");
                if (!string.IsNullOrEmpty(d)) sb.Append(": ").Append(d.Length > 90 ? d.Substring(0, 90) + "..." : d);
            }
            return sb.ToString();
        }

        static object Take(Dictionary<string, object> a, params string[] keys)
        {
            foreach (string want in keys)
            {
                string nw = ToolAliases.Norm(want);
                foreach (string k in new List<string>(a.Keys))
                {
                    if (k == want || ToolAliases.Norm(k) == nw)
                    {
                        object v = a[k];
                        a.Remove(k);
                        return v;
                    }
                }
            }
            return null;
        }

        static int ToInt(object o)
        {
            if (o is double) return (int)Math.Max(-int.MaxValue, Math.Min(int.MaxValue, (double)o));
            if (o is bool) return (bool)o ? 1 : 0;
            int n;
            string s = o as string;
            if (s != null && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            double d;
            if (s != null && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return (int)Math.Max(-int.MaxValue, Math.Min(int.MaxValue, d));
            return 0;
        }

        static readonly string[] HoldNames = { "hold_ms", "holdms", "hold", "hold_time", "hold_duration", "hold_for", "duration", "duration_ms", "hold_seconds", "hold_secs", "hold_sec", "duration_seconds", "seconds", "milliseconds", "ms", "press_duration", "time" };

        public static void HoldArgs(Dictionary<string, object> a, ref bool changed)
        {
            foreach (string name in HoldNames)
            {
                string found = null;
                foreach (string have in a.Keys) if (Regex.Replace(have.ToLowerInvariant(), @"[\s\-]", "_") == name) { found = have; break; }
                if (found == null) continue;
                object v = a[found];
                double n;
                string unit = "";
                if (!TryNum(v, out n))
                {
                    Match m = Regex.Match(Str(v).Trim().ToLowerInvariant(), @"^(\d+(?:\.\d+)?)\s*(ms|milliseconds?|s|secs?|seconds?)?$");
                    if (!m.Success) continue;
                    n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    unit = m.Groups[2].Value;
                }
                if (n <= 0) continue;
                bool secs = unit.StartsWith("s") || (unit.Length == 0 && (name.Contains("sec") || (!name.Contains("ms") && name != "milliseconds" && n <= 60)));
                int ms = (int)Math.Round(Math.Min(600000, secs ? n * 1000 : n));
                if (found == "hold_ms" && v is double && (double)v == ms) return;
                a.Remove(found);
                a["hold_ms"] = (double)ms;
                changed = true;
                return;
            }
        }

        static bool TryNum(object o, out double d)
        {
            d = 0;
            if (o is double) { d = (double)o; return true; }
            string s = o as string;
            return s != null && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        static bool ToBool(object o)
        {
            if (o is bool) return (bool)o;
            if (o is double) return (double)o != 0;
            string s = (o as string ?? "").Trim().ToLowerInvariant();
            return s == "true" || s == "yes" || s == "1" || s == "on";
        }

        static string Str(object o)
        {
            if (o == null) return "";
            string s = o as string;
            if (s != null) return s;
            if (o is double) return ((double)o).ToString(CultureInfo.InvariantCulture);
            return Json.Write(o);
        }

        static string JoinArgs(List<object> l)
        {
            StringBuilder sb = new StringBuilder();
            foreach (object o in l)
            {
                string s = Str(o);
                if (sb.Length > 0) sb.Append(' ');
                if (s.Length == 0 || (s.IndexOfAny(new char[] { ' ', '\t' }) >= 0 && !(s.StartsWith("\"") || s.StartsWith("'")))) sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else sb.Append(s);
            }
            return sb.ToString();
        }

        static string JoinLines(List<object> l)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append('\n'); sb.Append(Str(l[i])); }
            return sb.ToString();
        }

        static string Special(string tool, Dictionary<string, object> a, Dictionary<string, string> usedAlias, ref bool changed)
        {
            switch (tool)
            {
                case "ask_user":
                    {
                        object qs;
                        if (a.ContainsKey("question") || !a.TryGetValue("questions", out qs)) break;
                        List<object> ql = qs as List<object>;
                        if (ql == null && qs is string) ql = Json.TryParse((string)qs) as List<object>;
                        if (ql == null || ql.Count == 0) break;
                        object first = ql[0];
                        string q = (first as string) ?? Json.Str(first, "question") ?? Json.Str(first, "text") ?? Json.Str(first, "prompt");
                        if (string.IsNullOrEmpty(q)) break;
                        a["question"] = q;
                        object opts = Json.Get(first, "options");
                        if (opts != null && !a.ContainsKey("options")) a["options"] = opts;
                        a.Remove("questions");
                        changed = true;
                        break;
                    }
                case "read_file":
                    {
                        object vr = Take(a, "view_range", "range", "line_range", "lines_range");
                        List<object> vl = vr as List<object>;
                        if (vl == null && vr is string) vl = Json.TryParse((string)vr) as List<object>;
                        if (vl != null && vl.Count >= 1)
                        {
                            int s = Math.Max(1, ToInt(vl[0]));
                            a["offset"] = (double)s;
                            if (vl.Count >= 2 && ToInt(vl[1]) > 0) a["limit"] = (double)Math.Max(1, ToInt(vl[1]) - s + 1);
                            changed = true;
                        }
                        else if (vr is string) RangeInto((string)vr, a, ref changed);
                        object lim;
                        if (a.TryGetValue("limit", out lim) && lim is string && Regex.IsMatch((string)lim, @"^\s*\d+\s*[-:,]\s*\d+\s*$")) RangeInto((string)lim, a, ref changed);
                        object end = Take(a, "end_line", "line_end", "to_line", "last_line", "end", "until_line", "stop_line", "end_line_number", "endline", "to");
                        if (end != null)
                        {
                            int e = ToInt(end);
                            int s = a.ContainsKey("offset") ? Math.Max(1, ToInt(a["offset"])) : 1;
                            if (e >= s) { a["offset"] = (double)s; a["limit"] = (double)(e - s + 1); changed = true; }
                        }
                        break;
                    }
                case "write_file":
                    {
                        object c;
                        if (a.TryGetValue("content", out c) && c is List<object>) { a["content"] = JoinLines((List<object>)c); changed = true; }
                        else if (!a.ContainsKey("content"))
                        {
                            object ls = Take(a, "lines", "content_lines");
                            if (ls is List<object>) { a["content"] = JoinLines((List<object>)ls); changed = true; }
                        }
                        object ap;
                        if (a.TryGetValue("append", out ap) && !(ap is bool)) { a["append"] = ToBool(ap); changed = true; }
                        object mode = Take(a, "mode", "write_mode");
                        if (mode is string && ((string)mode).ToLowerInvariant().StartsWith("a") && !a.ContainsKey("append")) { a["append"] = true; changed = true; }
                        break;
                    }
                case "edit_file":
                    {
                        object ed = Take(a, "edits", "changes", "replacements", "operations", "patches", "modifications");
                        if (ed is string) ed = Json.TryParse((string)ed);
                        List<object> el = ed as List<object>;
                        if (el == null && ed is Dictionary<string, object>) el = new List<object>(new object[] { ed });
                        if (el != null)
                        {
                            List<object> norm = new List<object>();
                            foreach (object o in el)
                            {
                                Dictionary<string, object> d = o as Dictionary<string, object>;
                                if (d == null) continue;
                                Dictionary<string, object> nd = new Dictionary<string, object>(d);
                                object os = Take(nd, "old_string", "old_str", "old", "old_text", "search", "find", "original", "from", "before", "to_replace");
                                object ns = Take(nd, "new_string", "new_str", "new", "new_text", "replace", "replacement", "to", "after", "with");
                                object ra = Take(nd, "replace_all", "all", "global");
                                Dictionary<string, object> e = new Dictionary<string, object>();
                                if (os != null) e["old_string"] = Str(os);
                                if (ns != null) e["new_string"] = Str(ns);
                                if (ra != null) e["replace_all"] = ToBool(ra);
                                norm.Add(e);
                            }
                            a["edits"] = norm;
                            changed = true;
                        }
                        object ins = Take(a, "insert_line", "insert_after", "after_line", "line_after", "insert_at");
                        if (ins != null)
                        {
                            a["insert_line"] = (double)ToInt(ins);
                            if (!a.ContainsKey("new_string"))
                            {
                                object ns = Take(a, "new_str", "text", "content", "insert_text", "code");
                                if (ns != null) a["new_string"] = Str(ns);
                            }
                            changed = true;
                        }
                        break;
                    }
                case "search_text":
                    {
                        object ic = Take(a, "-i", "ignore_case", "case_insensitive", "insensitive", "nocase", "ignorecase");
                        if (ic != null && !a.ContainsKey("case_sensitive")) { a["case_sensitive"] = !ToBool(ic); changed = true; }
                        if (!a.ContainsKey("file_pattern"))
                        {
                            object ty = Take(a, "type", "file_type", "ext", "extension", "lang", "language", "filetype");
                            string glob = TypeToGlob(ty as string);
                            if (glob != null) { a["file_pattern"] = glob; changed = true; }
                        }
                        string qa;
                        if (usedAlias.TryGetValue("query", out qa) && (qa == "pattern" || qa == "regex" || qa == "regexp" || qa == "expression" || qa == "search_pattern") && !a.ContainsKey("regex"))
                        {
                            a["regex"] = true;
                            changed = true;
                        }
                        object om = Take(a, "output_mode", "output_format");
                        if (om != null)
                        {
                            string ms = (Str(om) ?? "").ToLowerInvariant();
                            if (!a.ContainsKey("output")) a["output"] = ms.Contains("file") || ms.Contains("count") ? "files" : "lines";
                            changed = true;
                        }
                        object cx = Take(a, "-C", "context_lines", "lines_of_context", "context_size");
                        object ca = Take(a, "-A", "after_context", "lines_after");
                        object cb = Take(a, "-B", "before_context", "lines_before");
                        if (cx != null || ca != null || cb != null)
                        {
                            if (!a.ContainsKey("context")) a["context"] = (double)Math.Max(cx != null ? ToInt(cx) : 0, Math.Max(ca != null ? ToInt(ca) : 0, cb != null ? ToInt(cb) : 0));
                            changed = true;
                        }
                        while (Take(a, "-n", "head_limit", "multiline", "max_results") != null) changed = true;
                        break;
                    }
                case "run_command":
                    {
                        object c;
                        if (a.TryGetValue("command", out c) && c is List<object>) { a["command"] = JoinArgs((List<object>)c); changed = true; }
                        object extra = Take(a, "args", "arguments", "argv");
                        if (extra != null && a.ContainsKey("command"))
                        {
                            a["command"] = Str(a["command"]) + " " + (extra is List<object> ? JoinArgs((List<object>)extra) : Str(extra));
                            changed = true;
                        }
                        object ms = Take(a, "timeout_ms", "timeout_millis", "timeoutms", "timeout_milliseconds");
                        if (ms != null && !a.ContainsKey("timeout_seconds")) { a["timeout_seconds"] = (double)Math.Max(1, ToInt(ms) / 1000); changed = true; }
                        object ts;
                        if (a.TryGetValue("timeout_seconds", out ts) && ToInt(ts) > 3600) { a["timeout_seconds"] = (double)Math.Max(1, ToInt(ts) / 1000); changed = true; }
                        object lang = Take(a, "language", "lang");
                        string ls = (lang as string ?? "").Trim().ToLowerInvariant();
                        if (lang != null) changed = true;
                        if (ls.Length > 0 && ls != "powershell" && ls != "pwsh" && ls != "ps1" && ls != "ps" && ls != "cmd" && ls != "batch" && ls != "bat" &&
                            ls != "shell" && ls != "bash" && ls != "sh" && ls != "console" && ls != "terminal" && ls != "zsh")
                            return "run_command runs PowerShell commands, not " + ls + " code. Write the code to a file with write_file (for example " + ScriptName(ls) + "), then run that file with run_command (for example: " + RunHint(ls) + ").";
                        break;
                    }
                case "command_status":
                    {
                        object id;
                        if (a.TryGetValue("id", out id) && id is string)
                        {
                            Match m = Regex.Match((string)id, @"(\d+)\s*$");
                            if (!m.Success) m = Regex.Match((string)id, @"(\d+)");
                            if (m.Success) { a["id"] = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); changed = true; }
                        }
                        object w;
                        if (a.TryGetValue("wait_seconds", out w) && ToInt(w) > 600) { a["wait_seconds"] = (double)(ToInt(w) / 1000); changed = true; }
                        break;
                    }
                case "browser_click":
                    NormRef(a, "e", ref changed);
                    ClickArgs(a, ref changed);
                    break;
                case "browser_hover":
                    NormRef(a, "e", ref changed);
                    break;
                case "browser_drag":
                    NormRef(a, "e", ref changed);
                    NormRefKey(a, "to_ref", "e", ref changed);
                    break;
                case "app_hover":
                    NormRef(a, "a", ref changed);
                    break;
                case "web_search":
                    {
                        object rv;
                        if (!a.TryGetValue("recent", out rv)) break;
                        string r = (Str(rv) ?? "").Trim().ToLowerInvariant().Replace("past", "").Replace("last", "").Replace("_", " ").Trim();
                        string nr = null;
                        if (r.Length == 0 || r == "any" || r == "all" || r == "none" || r == "anytime" || r == "false") nr = "";
                        else if (r == "d" || r == "pd" || r == "1d" || r == "24h" || r.Contains("day") || r.Contains("hour") || r == "today") nr = "day";
                        else if (r == "w" || r == "pw" || r == "7d" || r.Contains("week")) nr = "week";
                        else if (r == "m" || r == "pm" || r == "30d" || r == "31d" || r.Contains("month")) nr = "month";
                        else if (r == "y" || r == "py" || r == "365d" || r == "12m" || r.Contains("year")) nr = "year";
                        if (nr == null) break;
                        if (nr.Length == 0) a.Remove("recent");
                        else a["recent"] = nr;
                        changed = true;
                        break;
                    }
                case "app_scroll":
                    {
                        NormRef(a, "a", ref changed);
                        object d;
                        a.TryGetValue("direction", out d);
                        string ds = (d as string ?? "").Trim().ToLowerInvariant();
                        string nd = ds.Contains("left") ? "left" : ds.Contains("right") ? "right" : ds.Contains("up") || ds == "top" || ds == "backward" || ds == "back" ? "up" : "down";
                        object px = Take(a, "pixels");
                        if (px != null && !a.ContainsKey("notches"))
                        {
                            int pv = ToInt(px);
                            if (pv < 0 && d == null) nd = "up";
                            a["notches"] = (double)Math.Max(1, Math.Min(50, (Math.Abs(pv) + 50) / 100));
                            changed = true;
                        }
                        object nv;
                        if (a.TryGetValue("notches", out nv) && ToInt(nv) < 0)
                        {
                            if (d == null) nd = nd == "down" ? "up" : nd == "right" ? "left" : nd;
                            a["notches"] = (double)Math.Abs(ToInt(nv));
                            changed = true;
                        }
                        if (!(d is string) || (string)d != nd) { a["direction"] = nd; changed = true; }
                        break;
                    }
                case "app_drag":
                    NormRef(a, "a", ref changed);
                    NormRefKey(a, "to_ref", "a", ref changed);
                    break;
                case "browser_type":
                    NormRef(a, "e", ref changed);
                    break;
                case "app_click":
                    NormRef(a, "a", ref changed);
                    ClickArgs(a, ref changed);
                    break;
                case "app_type":
                    NormRef(a, "a", ref changed);
                    break;
                case "browser_press_key":
                case "app_key":
                    {
                        object k;
                        if (a.TryGetValue("key", out k) && k is List<object>)
                        {
                            List<object> kl = (List<object>)k;
                            bool combo = false;
                            foreach (object o in kl) { string ks = (Str(o) ?? "").Trim().ToLowerInvariant(); if (ks == "ctrl" || ks == "control" || ks == "shift" || ks == "alt" || ks == "meta" || ks == "cmd" || ks == "win") combo = true; }
                            a["key"] = JoinWith(kl, combo ? "+" : " ");
                            changed = true;
                        }
                        if (tool == "browser_press_key" && a.TryGetValue("key", out k) && k is string)
                        {
                            string nk = BrowserKey((string)k);
                            if (nk != (string)k) { a["key"] = nk; changed = true; }
                        }
                        HoldArgs(a, ref changed);
                        break;
                    }
                case "browser_scroll":
                    {
                        NormRef(a, "e", ref changed);
                        object d;
                        a.TryGetValue("direction", out d);
                        string ds = (d as string ?? "").Trim().ToLowerInvariant();
                        string nd = ds.Contains("left") ? "left" : ds.Contains("right") ? "right" : ds.Contains("up") || ds == "top" || ds == "backward" || ds == "back" ? "up" : "down";
                        object px;
                        if (a.TryGetValue("pixels", out px) && ToInt(px) < 0)
                        {
                            if (d == null) nd = "up";
                            a["pixels"] = (double)Math.Abs(ToInt(px));
                            changed = true;
                        }
                        if (!(d is string) || (string)d != nd) { a["direction"] = nd; changed = true; }
                        break;
                    }
                case "todo_write":
                    NormTodos(a, ref changed);
                    break;
                case "memory":
                    {
                        object act;
                        a.TryGetValue("action", out act);
                        string s = (act as string ?? "").Trim().ToLowerInvariant();
                        string ns = s;
                        if (s.Length == 0) ns = a.ContainsKey("fact") ? "add" : "list";
                        else if (s == "save" || s == "store" || s == "remember" || s == "create" || s == "append" || s == "write" || s == "set" || s == "insert" || s == "update" || s == "put" || s == "new" || s == "memorize") ns = "add";
                        else if (s == "delete" || s == "forget" || s == "erase" || s == "del" || s == "rm" || s == "drop") ns = "remove";
                        else if (s == "get" || s == "read" || s == "show" || s == "view" || s == "recall" || s == "all" || s == "ls" || s == "query" || s == "search" || s == "load") ns = "list";
                        if (ns != s || !(act is string)) { a["action"] = ns; changed = true; }
                        break;
                    }
            }
            return null;
        }

        static void RangeInto(string s, Dictionary<string, object> a, ref bool changed)
        {
            Match m = Regex.Match(s ?? "", @"(\d+)\s*[-:,]\s*(\d+)");
            if (!m.Success) return;
            int from = Math.Max(1, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)), to = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (to < from) return;
            a["offset"] = (double)from;
            a["limit"] = (double)(to - from + 1);
            changed = true;
        }

        static string ScriptName(string lang)
        {
            switch (lang)
            {
                case "python": case "py": case "python3": return "script.py";
                case "javascript": case "js": case "node": case "nodejs": return "script.js";
                case "typescript": case "ts": return "script.ts";
                case "ruby": case "rb": return "script.rb";
                case "php": return "script.php";
                case "csharp": case "c#": case "cs": return "Program.cs in a dotnet project";
                default: return "a script file";
            }
        }

        static string RunHint(string lang)
        {
            switch (lang)
            {
                case "python": case "py": case "python3": return "python script.py";
                case "javascript": case "js": case "node": case "nodejs": return "node script.js";
                case "typescript": case "ts": return "npx tsx script.ts";
                case "ruby": case "rb": return "ruby script.rb";
                case "php": return "php script.php";
                case "csharp": case "c#": case "cs": return "dotnet run";
                default: return "the interpreter followed by the file name";
            }
        }

        static string TypeToGlob(string t)
        {
            if (string.IsNullOrEmpty(t)) return null;
            string s = t.Trim().ToLowerInvariant();
            if (s.StartsWith("*")) return s;
            if (s.StartsWith(".")) return "*" + s;
            switch (s)
            {
                case "python": return "*.py";
                case "javascript": return "*.js";
                case "typescript": return "*.ts";
                case "csharp": case "c#": return "*.cs";
                case "c++": return "*.cpp";
                case "rust": return "*.rs";
                case "golang": return "*.go";
                case "markdown": return "*.md";
                case "powershell": return "*.ps1";
                case "ruby": return "*.rb";
                case "kotlin": return "*.kt";
            }
            if (Regex.IsMatch(s, "^[a-z0-9]{1,8}$")) return "*." + s;
            return null;
        }

        static string JoinWith(List<object> l, string sep)
        {
            List<string> parts = new List<string>();
            foreach (object o in l) { string x = Str(o).Trim(); if (x.Length > 0) parts.Add(x); }
            return string.Join(sep, parts.ToArray());
        }

        static void NormRef(Dictionary<string, object> a, string prefix, ref bool changed) { NormRefKey(a, "ref", prefix, ref changed); }

        static void NormRefKey(Dictionary<string, object> a, string key, string prefix, ref bool changed)
        {
            object r;
            if (!a.TryGetValue(key, out r) || r == null) return;
            string s = r is double ? ((int)(double)r).ToString(CultureInfo.InvariantCulture) : Str(r);
            string t = s.Trim().Trim('[', ']', '"', '\'', '#', '(', ')', ' ');
            string res = null;
            Match m = Regex.Match(t, @"\b" + prefix + @"(\d+)\b", RegexOptions.IgnoreCase);
            if (m.Success) res = prefix + m.Groups[1].Value;
            else
            {
                Match d = Regex.Match(t, @"^(?:ref\s*[=:]?\s*)?(\d+)$", RegexOptions.IgnoreCase);
                if (d.Success) res = prefix + d.Groups[1].Value;
            }
            if (res != null && res != s) { a[key] = res; changed = true; }
        }

        static void ClickArgs(Dictionary<string, object> a, ref bool changed)
        {
            object mb = Take(a, "mouse_button", "which_button", "btn");
            if (mb != null && !a.ContainsKey("button")) { a["button"] = mb; changed = true; }
            object ct = Take(a, "click_type", "kind", "action");
            string cts = (ct as string ?? "").ToLowerInvariant();
            if (cts.Contains("double") && !a.ContainsKey("double")) { a["double"] = true; changed = true; }
            if (cts.Contains("triple") && !a.ContainsKey("clicks")) { a["clicks"] = 3.0; changed = true; }
            if (cts.Contains("right") && !a.ContainsKey("button")) { a["button"] = "right"; changed = true; }
            if (cts.Contains("middle") && !a.ContainsKey("button")) { a["button"] = "middle"; changed = true; }
            object b;
            if (a.TryGetValue("button", out b) && b is double) { a["button"] = (double)b >= 3 ? "middle" : (double)b == 2 ? "right" : "left"; changed = true; }
            object mods;
            if (a.TryGetValue("modifiers", out mods) && mods is List<object>) { a["modifiers"] = JoinWith((List<object>)mods, "+"); changed = true; }
            Coords(a, ref changed);
        }

        static void Coords(Dictionary<string, object> a, ref bool changed)
        {
            if (a.ContainsKey("x") && a.ContainsKey("y")) return;
            object c = Take(a, "coordinate", "coordinates", "position", "point", "pos", "xy", "at", "location", "coords");
            if (c == null) return;
            double x, y;
            List<object> l = c as List<object>;
            if (l == null && c is string && ((string)c).TrimStart().StartsWith("[")) l = Json.TryParse((string)c) as List<object>;
            if (l != null && l.Count >= 2 && TryNum(l[0], out x) && TryNum(l[1], out y)) { a["x"] = x; a["y"] = y; changed = true; return; }
            Dictionary<string, object> d = c as Dictionary<string, object>;
            if (d != null && TryNum(Json.Get(d, "x"), out x) && TryNum(Json.Get(d, "y"), out y)) { a["x"] = x; a["y"] = y; changed = true; return; }
            string s = c as string;
            if (s != null)
            {
                Match m = Regex.Match(s, @"(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)");
                if (m.Success)
                {
                    a["x"] = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    a["y"] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    changed = true;
                }
            }
        }

        static string BrowserKey(string k)
        {
            string s = k.Trim();
            string l = s.ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", "");
            switch (l)
            {
                case "enter": case "return": case "\n": return "Enter";
                case "tab": return "Tab";
                case "esc": case "escape": return "Escape";
                case "backspace": case "back": return "Backspace";
                case "delete": case "del": return "Delete";
                case "space": case "spacebar": return "Space";
                case "up": case "arrowup": case "uparrow": return "ArrowUp";
                case "down": case "arrowdown": case "downarrow": return "ArrowDown";
                case "left": case "arrowleft": case "leftarrow": return "ArrowLeft";
                case "right": case "arrowright": case "rightarrow": return "ArrowRight";
                case "pageup": case "pgup": return "PageUp";
                case "pagedown": case "pgdn": case "pgdown": return "PageDown";
                case "home": return "Home";
                case "end": return "End";
            }
            if (s == " ") return "Space";
            return s;
        }

        static void NormTodos(Dictionary<string, object> a, ref bool changed)
        {
            object t;
            if (!a.TryGetValue("todos", out t) || t == null) return;
            if (t is string)
            {
                object parsed = Json.TryParse((string)t);
                if (parsed is List<object>) t = parsed;
                else if (parsed is Dictionary<string, object>) t = new List<object>(new object[] { parsed });
                else t = LinesToTodos((string)t);
                a["todos"] = t;
                changed = true;
            }
            else if (t is Dictionary<string, object>)
            {
                Dictionary<string, object> d = (Dictionary<string, object>)t;
                if (d.ContainsKey("content") || d.ContainsKey("task") || d.ContainsKey("title")) t = new List<object>(new object[] { d });
                else
                {
                    List<object> l = new List<object>();
                    foreach (KeyValuePair<string, object> kv in d) l.Add(kv.Value is string ? (object)Json.O("content", kv.Value, "status", "pending") : kv.Value);
                    t = l;
                }
                a["todos"] = t;
                changed = true;
            }
            List<object> items = t as List<object>;
            if (items == null) return;
            foreach (object o in items)
            {
                Dictionary<string, object> it = o as Dictionary<string, object>;
                if (it == null) continue;
                if (!it.ContainsKey("content"))
                {
                    object v = Take(it, "task", "title", "description", "name", "text", "item", "step", "todo", "label", "summary");
                    if (v != null) { it["content"] = Str(v); changed = true; }
                }
                if (!it.ContainsKey("status"))
                {
                    object st = Take(it, "state", "done", "completed", "checked", "complete");
                    if (st is bool) { it["status"] = (bool)st ? "completed" : "pending"; changed = true; }
                    else if (st is string) { it["status"] = st; changed = true; }
                }
            }
        }

        static List<object> LinesToTodos(string s)
        {
            List<object> l = new List<object>();
            foreach (string raw in s.Replace("\r", "").Split('\n'))
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                t = Regex.Replace(t, @"^([-*\u2022]|\d+[.)])\s*", "");
                string status = "pending";
                Match m = Regex.Match(t, @"^\[(.)\]\s*");
                if (m.Success)
                {
                    string mark = m.Groups[1].Value.ToLowerInvariant();
                    status = mark == "x" || mark == "\u2713" || mark == "\u2714" ? "completed" : mark == ">" || mark == "~" || mark == "-" || mark == "/" ? "in_progress" : "pending";
                    t = t.Substring(m.Length);
                }
                else if (t.StartsWith("\u2713") || t.StartsWith("\u2714") || t.StartsWith("\u2705")) { status = "completed"; t = t.Substring(1).Trim(); }
                if (t.Length > 0) l.Add(Json.O("content", t, "status", status));
            }
            return l;
        }
    }

    static class EditAssist
    {
        public static List<int> MatchLines(string text, string find, int max)
        {
            List<int> l = new List<int>();
            if (string.IsNullOrEmpty(find)) return l;
            int i = 0, line = 1, scanned = 0;
            while (l.Count < max && (i = text.IndexOf(find, i, StringComparison.Ordinal)) >= 0)
            {
                for (int k = scanned; k < i; k++) if (text[k] == '\n') line++;
                scanned = i;
                l.Add(line);
                i += Math.Max(1, find.Length);
            }
            return l;
        }

        public static string ClosestBlock(string text, string oldS, int context, out int bestLine)
        {
            bestLine = 0;
            string[] lines = text.Split('\n');
            List<string> want = new List<string>(oldS.Split('\n'));
            while (want.Count > 0 && want[0].Trim().Length == 0) want.RemoveAt(0);
            while (want.Count > 0 && want[want.Count - 1].Trim().Length == 0) want.RemoveAt(want.Count - 1);
            if (want.Count == 0 || lines.Length == 0) return null;
            if (want.Count > 80) want = want.GetRange(0, 80);
            int n = want.Count;
            HashSet<string>[] wt = new HashSet<string>[n];
            int anchor = 0, best = -1;
            for (int k = 0; k < n; k++)
            {
                wt[k] = Fuzzy.Tokens(want[k].Trim());
                if (want[k].Trim().Length > want[anchor].Trim().Length) anchor = k;
            }
            HashSet<string>[] ft = new HashSet<string>[lines.Length];
            List<KeyValuePair<double, int>> anchors = new List<KeyValuePair<double, int>>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i >= 20000) break;
                ft[i] = Fuzzy.Tokens(lines[i].Trim());
                double s = Fuzzy.Similarity(ft[i], wt[anchor]);
                if (s > 0.2) anchors.Add(new KeyValuePair<double, int>(s, i));
            }
            anchors.Sort(delegate (KeyValuePair<double, int> x, KeyValuePair<double, int> y) { return y.Key.CompareTo(x.Key); });
            double bestScore = 0;
            for (int ai = 0; ai < anchors.Count && ai < 12; ai++)
            {
                int start = anchors[ai].Value - anchor;
                if (start < 0) start = 0;
                if (start + n > lines.Length) start = Math.Max(0, lines.Length - n);
                double sum = 0;
                int cnt = 0;
                for (int k = 0; k < n && start + k < lines.Length; k++)
                {
                    HashSet<string> f = ft[start + k] ?? Fuzzy.Tokens(lines[start + k].Trim());
                    sum += Fuzzy.Similarity(f, wt[k]);
                    cnt++;
                }
                double score = cnt == 0 ? 0 : sum / n;
                if (score > bestScore) { bestScore = score; best = start; }
            }
            if (best < 0 || bestScore < 0.35) return null;
            bestLine = best + 1;
            int from = Math.Max(1, best + 1 - context), to = Math.Min(lines.Length, best + n + context);
            return ToolKit.Numbered(text, from, to);
        }

        public static string FirstDifference(string text, string oldS, int bestLine)
        {
            if (bestLine <= 0) return null;
            string[] lines = text.Split('\n');
            List<string> want = new List<string>(oldS.Split('\n'));
            while (want.Count > 0 && want[0].Trim().Length == 0) want.RemoveAt(0);
            for (int k = 0; k < want.Count && bestLine - 1 + k < lines.Length; k++)
            {
                string f = lines[bestLine - 1 + k], w = want[k];
                if (f == w) continue;
                if (f.Trim() == w.Trim()) return "line " + (bestLine + k) + " differs only in indentation or trailing spaces";
                return "line " + (bestLine + k) + " in the file is \"" + Clip(f.Trim(), 120) + "\" but your old_string has \"" + Clip(w.Trim(), 120) + "\"";
            }
            return null;
        }

        static string Clip(string s, int n) { return s.Length > n ? s.Substring(0, n) + "..." : s; }
    }

    static class WriteCheck
    {
        static readonly Regex Strong = new Regex(
            @"\b(rest of (the |this |your |my )?(code|file|class|function|functions|method|methods|component|implementation|content|contents|script|page|styles?|module|logic|body|html|markup|document|program|tests?|properties|fields|members|cases|items|lines|steps|config|configuration|imports)|" +
            @"(remaining|existing|other|previous|original|old) (code|methods|functions|content|implementation|logic|imports|styles?|properties|fields|members|tests|html|markup|cases|lines|items|sections|parts|rules)\s*(here|unchanged|remains?|stays?|stay|is|are|omitted|goes|go|kept|as\b|\.\.\.|\u2026|\)|\]|$)|" +
            @"keep (the )?(existing|rest|original|other|remaining|previous)\b|omitted for brevity|truncated for brevity|for brevity|" +
            @"(code|content|lines|methods|functions|implementation) (omitted|truncated|elided|not shown)|no changes? (below|above|to the rest)|" +
            @"^\(?\s*(unchanged|no changes?|same as before|existing code|existing implementation|previous code)\s*\)?\.?$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Dots = new Regex(@"(\.\.\.|\u2026)", RegexOptions.CultureInvariant);
        static readonly Regex Weak = new Regex(@"\b(rest|remaining|existing|unchanged|same|previous|omitted|other|more|etc|here|implementation|code|methods|functions|content|stuff|before|above|below)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex DotsOnly = new Regex(@"^(//+|#+|/\*+|<!--|--|\{/\*+)\s*[\[(]?\s*(\.\.\.|\u2026)\s*[\])]?\s*(\*+/\}?|-->)?$", RegexOptions.CultureInvariant);

        public static string Placeholder(string newText, string oldText, string fileName)
        {
            if (string.IsNullOrEmpty(newText)) return null;
            string ext = System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant();
            bool prose = ext == ".md" || ext == ".markdown" || ext == ".txt" || ext == ".rst" || ext == ".log" || ext == "";
            HashSet<string> old = new HashSet<string>(StringComparer.Ordinal);
            if (oldText != null) foreach (string l in oldText.Split('\n')) old.Add(l.Trim());
            foreach (string raw in newText.Split('\n'))
            {
                string t = raw.Trim();
                if (t.Length < 3 || t.Length > 200 || old.Contains(t)) continue;
                if (!prose && DotsOnly.IsMatch(t)) return t;
                string body = CommentBody(t, prose);
                if (body == null) continue;
                if (Strong.IsMatch(body)) return t;
                if (Dots.IsMatch(body) && Weak.IsMatch(body)) return t;
            }
            return null;
        }

        static string CommentBody(string t, bool prose)
        {
            string[] starts = { "{/*", "<!--", "///", "//", "/*", "--", "#", "*", "rem ", "REM ", "::" };
            foreach (string s in starts)
            {
                if (!t.StartsWith(s, StringComparison.Ordinal)) continue;
                if (prose && s != "<!--") return null;
                if (s == "*" && !(t.Length > 1 && t[1] == ' ')) return null;
                if (s == "#" && (t.StartsWith("#include") || t.StartsWith("#define") || t.StartsWith("#if") || t.StartsWith("#!") || t.StartsWith("#region") || t.StartsWith("#endregion") || t.StartsWith("#pragma"))) return null;
                string b = t.Substring(s.Length).Trim();
                if (b.EndsWith("*/}")) b = b.Substring(0, b.Length - 3);
                else if (b.EndsWith("*/") || b.EndsWith("-->")) b = b.Substring(0, b.Length - (b.EndsWith("*/") ? 2 : 3));
                return b.Trim();
            }
            return null;
        }

        public static string RefusalForWrite(string line, string name)
        {
            return "Not written: your content contains the placeholder line \"" + line + "\" instead of real code. Writing it would delete that code from " + name +
                   ". Use edit_file to change only the parts that need changing, or send the complete file content with no placeholders.";
        }

        public static string RefusalForEdit(string line)
        {
            return "Not edited: new_string contains the placeholder line \"" + line + "\" instead of real code, so the code it stands for would be deleted. " +
                   "Put the real code in new_string, or make old_string smaller so it covers only the lines you are changing.";
        }

        public static string Problems(string fileName, string before, string after, bool partial)
        {
            if (after == null || after.Length > 3 * 1024 * 1024) return null;
            string ext = System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant();
            string name = System.IO.Path.GetFileName(fileName ?? "").ToLowerInvariant();
            try
            {
                if (ext == ".json" && !partial && !Jsonc(name, after))
                {
                    string e = JsonProblem(after);
                    if (e != null && (before == null || before.Trim().Length == 0 || JsonProblem(before) == null))
                        return "Warning: the file is no longer valid JSON (" + e + "). Fix it before you continue.";
                    return null;
                }
                if (IsXml(ext) && !partial)
                {
                    string e = XmlProblem(after);
                    if (e != null && (before == null || before.Trim().Length == 0 || XmlProblem(before) == null))
                        return "Warning: the file is no longer well-formed XML (" + e.TrimEnd('.') + "). Fix it before you continue.";
                    return null;
                }
                Lang lang = LangOf(ext);
                if (lang == null) return null;
                string b = Brackets(after, lang);
                if (b == null) return null;
                if (before != null && before.Trim().Length > 0 && Brackets(before, lang) != null) return null;
                if (partial) return "Note: the brackets do not balance yet (" + b + "). That is expected if more parts follow.";
                return "Warning: the brackets in the file do not balance any more: " + b + ". The file may be cut off or broken - check that part.";
            }
            catch { return null; }
        }

        static bool Jsonc(string name, string text)
        {
            if (name == "tsconfig.json" || name == "jsconfig.json" || name.StartsWith("tsconfig.") || name == "settings.json" || name == "launch.json" || name == "tasks.json" ||
                name == "extensions.json" || name == "devcontainer.json" || name == ".eslintrc.json" || name == "keybindings.json" || name.EndsWith(".code-workspace")) return true;
            return Regex.IsMatch(text, @"(^|\n)\s*(//|/\*)", RegexOptions.CultureInvariant);
        }

        static bool IsXml(string ext)
        {
            switch (ext)
            {
                case ".xml": case ".csproj": case ".vbproj": case ".fsproj": case ".vcxproj": case ".props": case ".targets": case ".config": case ".xaml":
                case ".resx": case ".svg": case ".plist": case ".nuspec": case ".xsd": case ".xsl": case ".xslt": case ".axaml": case ".manifest": case ".wxs":
                    return true;
            }
            return false;
        }

        public static string JsonProblem(string text)
        {
            string t = text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
            if (t.Trim().Length == 0) return null;
            try { Json.Parse(t); return null; }
            catch (FormatException ex)
            {
                string msg = ex.Message;
                Match m = Regex.Match(msg, @"position (\d+)");
                int pos = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : t.Length;
                int line = 1, col = 1;
                for (int i = 0; i < pos && i < t.Length; i++) { if (t[i] == '\n') { line++; col = 1; } else col++; }
                string clean = Regex.Replace(msg, @"\s*at position \d+", "").Trim().TrimEnd('.');
                return clean + " at line " + line + ", column " + col;
            }
            catch { return null; }
        }

        public static string XmlProblem(string text)
        {
            try
            {
                XmlReaderSettings s = new XmlReaderSettings();
                s.DtdProcessing = DtdProcessing.Ignore;
                s.XmlResolver = null;
                s.IgnoreComments = true;
                s.MaxCharactersFromEntities = 1024;
                using (XmlReader r = XmlReader.Create(new StringReader(text), s)) { while (r.Read()) { } }
                return null;
            }
            catch (XmlException ex) { return ex.Message; }
            catch { return null; }
        }

        class Lang
        {
            public bool Slash, Block, Hash, PsBlock, Triple, Backtick, SingleIsChar, SingleString, MultiLine, Verbatim, HereString;
        }

        static Lang LangOf(string ext)
        {
            Lang l = new Lang();
            switch (ext)
            {
                case ".c": case ".h": case ".cc": case ".cpp": case ".cxx": case ".hpp": case ".hh": case ".m": case ".mm":
                    l.Slash = l.Block = l.SingleIsChar = true; return l;
                case ".cs":
                    l.Slash = l.Block = l.SingleIsChar = l.Verbatim = l.Triple = true; return l;
                case ".java": case ".kt": case ".kts": case ".scala": case ".groovy":
                    l.Slash = l.Block = l.SingleIsChar = l.Triple = true; return l;
                case ".go":
                    l.Slash = l.Block = l.SingleIsChar = l.Backtick = true; return l;
                case ".rs":
                    l.Slash = l.Block = l.SingleIsChar = true; return l;
                case ".swift":
                    l.Slash = l.Block = l.Triple = true; return l;
                case ".js": case ".mjs": case ".cjs": case ".jsx": case ".ts": case ".tsx": case ".mts": case ".cts":
                    l.Slash = l.Block = l.SingleString = l.Backtick = true; return l;
                case ".dart":
                    l.Slash = l.Block = l.SingleString = l.Triple = true; return l;
                case ".php":
                    l.Slash = l.Block = l.Hash = l.SingleString = l.MultiLine = true; return l;
                case ".py": case ".pyw":
                    l.Hash = l.Triple = l.SingleString = true; return l;
                case ".ps1": case ".psm1": case ".psd1":
                    l.Hash = l.PsBlock = l.SingleString = l.MultiLine = l.HereString = true; return l;
                case ".css":
                    l.Block = l.SingleString = true; return l;
                case ".scss": case ".less":
                    l.Slash = l.Block = l.SingleString = true; return l;
            }
            return null;
        }

        static string Brackets(string text, Lang lang)
        {
            Stack<KeyValuePair<char, int>> st = new Stack<KeyValuePair<char, int>>();
            int line = 1, i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '\n') { line++; i++; continue; }
                char nx = i + 1 < n ? text[i + 1] : '\0';
                if (lang.Slash && c == '/' && nx == '/') { while (i < n && text[i] != '\n') i++; continue; }
                if (lang.Block && c == '/' && nx == '*')
                {
                    i += 2;
                    while (i < n && !(text[i] == '*' && i + 1 < n && text[i + 1] == '/')) { if (text[i] == '\n') line++; i++; }
                    i += 2;
                    continue;
                }
                if (lang.PsBlock && c == '<' && nx == '#')
                {
                    i += 2;
                    while (i < n && !(text[i] == '#' && i + 1 < n && text[i + 1] == '>')) { if (text[i] == '\n') line++; i++; }
                    i += 2;
                    continue;
                }
                if (lang.Hash && c == '#') { while (i < n && text[i] != '\n') i++; continue; }
                if (lang.HereString && c == '@' && (nx == '"' || nx == '\'') && i + 2 < n && (text[i + 2] == '\n' || text[i + 2] == '\r'))
                {
                    char q = nx;
                    i += 2;
                    while (i < n && !(text[i] == '\n' && i + 2 < n && text[i + 1] == q && text[i + 2] == '@')) { if (text[i] == '\n') line++; i++; }
                    if (i < n) { line++; i += 3; }
                    continue;
                }
                if (lang.Triple && (c == '"' || c == '\'') && nx == c && i + 2 < n && text[i + 2] == c)
                {
                    if (c == '\'' && !lang.SingleString) { i++; continue; }
                    i += 3;
                    while (i < n && !(text[i] == c && i + 2 < n && text[i + 1] == c && text[i + 2] == c)) { if (text[i] == '\\') i++; else if (text[i] == '\n') line++; i++; }
                    i += 3;
                    continue;
                }
                if (c == '"')
                {
                    bool verb = lang.Verbatim && i > 0 && (text[i - 1] == '@' || (text[i - 1] == '$' && i > 1 && text[i - 2] == '@'));
                    i++;
                    while (i < n)
                    {
                        char d = text[i];
                        if (verb) { if (d == '"') { if (i + 1 < n && text[i + 1] == '"') { i += 2; continue; } break; } if (d == '\n') line++; i++; continue; }
                        if (d == '\\' || (lang.HereString && d == '`')) { i += 2; continue; }
                        if (d == '"') break;
                        if (d == '\n') { if (!lang.MultiLine) break; line++; }
                        i++;
                    }
                    if (i < n && text[i] == '"') i++;
                    continue;
                }
                if (c == '\'')
                {
                    if (lang.SingleIsChar)
                    {
                        if (nx == '\\') { int close = text.IndexOf('\'', i + 2); if (close > 0 && close - i <= 12 && text.IndexOf('\n', i, close - i) < 0) { i = close + 1; continue; } }
                        else if (i + 2 < n && text[i + 2] == '\'' && nx != '\n') { i += 3; continue; }
                        i++;
                        continue;
                    }
                    if (lang.SingleString)
                    {
                        i++;
                        while (i < n)
                        {
                            char d = text[i];
                            if (d == '\\' && !lang.HereString) { i += 2; continue; }
                            if (d == '\'') { if (lang.HereString && i + 1 < n && text[i + 1] == '\'') { i += 2; continue; } break; }
                            if (d == '\n') { if (!lang.MultiLine) break; line++; }
                            i++;
                        }
                        if (i < n && text[i] == '\'') i++;
                        continue;
                    }
                    i++;
                    continue;
                }
                if (c == '`' && lang.Backtick)
                {
                    i++;
                    while (i < n && text[i] != '`') { if (text[i] == '\\') i++; else if (text[i] == '\n') line++; i++; }
                    i++;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') st.Push(new KeyValuePair<char, int>(c, line));
                else if (c == ')' || c == ']' || c == '}')
                {
                    char want = c == ')' ? '(' : c == ']' ? '[' : '{';
                    if (st.Count == 0) return "there is an extra '" + c + "' at line " + line;
                    KeyValuePair<char, int> top = st.Pop();
                    if (top.Key != want) return "'" + c + "' at line " + line + " does not match the '" + top.Key + "' opened at line " + top.Value;
                }
                i++;
            }
            if (st.Count == 0) return null;
            KeyValuePair<char, int> last = st.Peek();
            return st.Count == 1 ? "the '" + last.Key + "' opened at line " + last.Value + " is never closed" :
                   st.Count + " brackets are never closed (the innermost is the '" + last.Key + "' opened at line " + last.Value + ")";
        }
    }

    static class CommandHelp
    {
        public const string BuiltInDocuments = "MinGent writes PDF, Word, PowerPoint and Excel files itself: write Markdown with write_file to a path ending in .pdf, .docx or .pptx, or CSV, a Markdown table or JSON rows to .xlsx.";

        static readonly HashSet<string> DocumentModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "reportlab", "fpdf", "fpdf2", "docx", "docxtpl", "openpyxl", "xlsxwriter", "pptx", "weasyprint", "pdfkit", "xhtml2pdf", "md2pdf", "pypandoc", "docx2pdf" };

        static readonly Regex DocumentInstall = new Regex(@"\b(pip3?|uv|poetry|conda|npm|pnpm|yarn|bun)(\.exe|\.cmd)?\b[^;&|\r\n]*?\b(install|add|i)\b[^;&|\r\n]*?(?<![\w.-])(reportlab|fpdf2?|python-docx|docxtpl|openpyxl|xlsxwriter|python-pptx|pdfkit|weasyprint|xhtml2pdf|md2pdf|markdown-pdf|docx2pdf|pypandoc|jspdf|pdfmake|pptxgenjs|exceljs|docx|xlsx)(?![\w.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex DocumentConvert = new Regex(@"\bpandoc(\.exe)?\b[^;&|\r\n]*?(-o|--output)[\s=]+[""']?[^\s""';&|]+\.(pdf|docx|pptx)\b|\b(soffice|libreoffice)(\.exe|\.com)?\b[^;&|\r\n]*?--convert-to\s+[""']?(pdf|docx|pptx|xlsx)\b|\bwkhtmltopdf(\.exe)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string DocumentTool(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            Match m = DocumentInstall.Match(cmd);
            if (m.Success) return m.Groups[4].Value;
            m = DocumentConvert.Match(cmd);
            if (!m.Success) return null;
            string v = m.Value.TrimStart().ToLowerInvariant();
            return v.StartsWith("pandoc") ? "pandoc" : v.StartsWith("wkhtmltopdf") ? "wkhtmltopdf" : "LibreOffice";
        }

        public static bool DocumentModule(string module) { return module != null && DocumentModules.Contains(module); }

        public static string TranslateChain(string cmd)
        {
            if (string.IsNullOrEmpty(cmd) || cmd.IndexOf("&&", StringComparison.Ordinal) < 0 && cmd.IndexOf("||", StringComparison.Ordinal) < 0) return null;
            List<string> parts = new List<string>();
            List<string> ops = new List<string>();
            StringBuilder cur = new StringBuilder();
            char q = '\0';
            int depth = 0;
            for (int i = 0; i < cmd.Length; i++)
            {
                char c = cmd[i];
                if (q != '\0')
                {
                    cur.Append(c);
                    if (q == '"' && c == '`' && i + 1 < cmd.Length) { cur.Append(cmd[++i]); continue; }
                    if (c == q)
                    {
                        if (i + 1 < cmd.Length && cmd[i + 1] == q) { cur.Append(cmd[++i]); continue; }
                        q = '\0';
                    }
                    continue;
                }
                if (c == '\'' || c == '"') { q = c; cur.Append(c); continue; }
                if (c == '`' && i + 1 < cmd.Length) { cur.Append(c).Append(cmd[++i]); continue; }
                if (c == '\n' || c == '\r') return null;
                if (c == '(' || c == '{' || c == '[') depth++;
                else if ((c == ')' || c == '}' || c == ']') && depth > 0) depth--;
                if (depth == 0 && i + 1 < cmd.Length && ((c == '&' && cmd[i + 1] == '&') || (c == '|' && cmd[i + 1] == '|')))
                {
                    parts.Add(cur.ToString());
                    ops.Add(c == '&' ? "&&" : "||");
                    cur.Length = 0;
                    i++;
                    continue;
                }
                cur.Append(c);
            }
            if (q != '\0') return null;
            parts.Add(cur.ToString());
            if (ops.Count == 0) return null;
            foreach (string o in ops) if (o != ops[0]) return null;
            foreach (string p in parts) if (p.Trim().Length == 0) return null;
            string cond = ops[0] == "&&" ? "if ($?) { " : "if (-not $?) { ";
            string res = parts[parts.Count - 1].Trim();
            for (int i = parts.Count - 2; i >= 0; i--) res = parts[i].Trim() + "; " + cond + res + " }";
            return res;
        }

        static readonly Regex NotCmdlet = new Regex(@"The term '([^']+)' is not recognized as (?:the )?name of a cmdlet|The term '([^']+)' is not recognized as a name of a cmdlet", RegexOptions.IgnoreCase);
        static readonly Regex NotCmd = new Regex(@"'([^']+)' is not recognized as an internal or external command", RegexOptions.IgnoreCase);
        static readonly Regex BadParam = new Regex(@"A parameter cannot be found that matches parameter name '([^']+)'", RegexOptions.IgnoreCase);
        static readonly Regex PyModule = new Regex(@"ModuleNotFoundError: No module named '([\w\.]+)'", RegexOptions.CultureInvariant);
        static readonly Regex NpmScript = new Regex(@"(?:npm ERR!|npm error) Missing script: ""?([\w:\-\.]+)""?", RegexOptions.IgnoreCase);
        static readonly Regex Prompt = new Regex(@"(\(y/n\)|\[y/n\]|\(yes/no\)|\[yes/no\]|password\s*:|passphrase|press any key|press enter|continue\?|are you sure|overwrite\b.*\?|username for|enter (a |the )?(value|choice|selection|number|name)|select an option|choose an option|\(Y/n\)|\[Y/n\]|\[y/N\]|\? \u203a|\?\s*$)", RegexOptions.IgnoreCase);
        static readonly Regex LocalUrl = new Regex(@"https?://(localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1?\])(:\d{2,5})?(/[^\s'""<>)\]]*)?", RegexOptions.IgnoreCase);

        static readonly Dictionary<string, string> BashToPs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "grep", "Select-String -Pattern 'text' -Path *.txt (or the search_text tool)" },
            { "egrep", "Select-String -Pattern 'regex' -Path *.txt (or the search_text tool)" },
            { "touch", "New-Item -ItemType File -Force name (or write_file)" },
            { "which", "Get-Command name" },
            { "export", "$env:NAME = 'value'" },
            { "head", "Get-Content file -TotalCount 20" },
            { "tail", "Get-Content file -Tail 20" },
            { "sed", "(Get-Content f) -replace 'old','new' | Set-Content f (or edit_file)" },
            { "awk", "PowerShell (Select-String, ForEach-Object, -split) or a small script" },
            { "wc", "(Get-Content file | Measure-Object -Line).Lines" },
            { "unzip", "Expand-Archive file.zip -DestinationPath folder" },
            { "zip", "Compress-Archive -Path folder -DestinationPath file.zip" },
            { "source", ". .\\script.ps1" },
            { "chmod", "nothing: Windows does not need it" },
            { "chown", "nothing: Windows does not need it" },
            { "sudo", "nothing: it does not exist here. If administrator rights are needed, tell the user" },
            { "python3", "python (or py)" },
            { "pip3", "python -m pip" },
            { "open", "Start-Process file" },
            { "xdg-open", "Start-Process file" },
            { "ln", "New-Item -ItemType SymbolicLink -Path link -Target target" },
            { "find", "Get-ChildItem -Recurse -Filter *.py (or the find_files tool)" },
            { "nano", "nothing: there is no terminal editor; use edit_file" },
            { "vim", "nothing: there is no terminal editor; use edit_file" },
            { "vi", "nothing: there is no terminal editor; use edit_file" },
            { "ifconfig", "ipconfig" },
            { "printenv", "Get-ChildItem env:" },
            { "env", "Get-ChildItem env:" },
            { "uname", "the system_info tool" },
            { "df", "Get-PSDrive -PSProvider FileSystem" },
            { "du", "Get-ChildItem -Recurse | Measure-Object -Property Length -Sum" },
            { "free", "the system_info tool" },
            { "killall", "Stop-Process -Name name" },
            { "pkill", "Stop-Process -Name name" },
            { "lsof", "Get-NetTCPConnection -LocalPort 3000" },
            { "make", "the build tool the project uses, or install make first" },
            { "true", "$true" },
        };

        static readonly Dictionary<string, string> PipNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "cv2", "opencv-python" }, { "PIL", "pillow" }, { "sklearn", "scikit-learn" }, { "yaml", "pyyaml" }, { "bs4", "beautifulsoup4" },
            { "dotenv", "python-dotenv" }, { "win32api", "pywin32" }, { "win32con", "pywin32" }, { "serial", "pyserial" }, { "docx", "python-docx" },
            { "fitz", "pymupdf" }, { "Crypto", "pycryptodome" }, { "jwt", "pyjwt" }, { "magic", "python-magic" }, { "attr", "attrs" },
        };

        public static string Hint(string cmd, string output, int exitCode, bool timedOut, string shell)
        {
            string o = output ?? "";
            List<string> hints = new List<string>();
            bool ps = shell != "cmd";
            Match m = NotCmdlet.Match(o);
            string missing = null;
            if (m.Success) missing = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value;
            else { m = NotCmd.Match(o); if (m.Success) missing = m.Groups[1].Value; }
            if (missing != null)
            {
                string bare = System.IO.Path.GetFileNameWithoutExtension(missing.Trim());
                string alt;
                if (BashToPs.TryGetValue(bare, out alt)) hints.Add("'" + bare + "' is a Linux command. On Windows use " + alt + ".");
                else if (bare.Equals("python", StringComparison.OrdinalIgnoreCase) || bare.Equals("pip", StringComparison.OrdinalIgnoreCase)) hints.Add("Python is not on PATH. Try 'py' (the Python launcher); if that is missing too, Python is not installed - tell the user.");
                else if (bare.Equals("node", StringComparison.OrdinalIgnoreCase) || bare.Equals("npm", StringComparison.OrdinalIgnoreCase) || bare.Equals("npx", StringComparison.OrdinalIgnoreCase)) hints.Add("Node.js is not installed or not on PATH. Tell the user if it is needed.");
                else if (bare.Equals("git", StringComparison.OrdinalIgnoreCase)) hints.Add("Git is not installed or not on PATH.");
                else if (bare.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) hints.Add("The .NET SDK is not installed or not on PATH.");
                else if (missing.StartsWith(".") || missing.IndexOf('\\') >= 0 || missing.IndexOf('/') >= 0) hints.Add("The file '" + missing + "' was not found. Check the path relative to the working folder (list_directory).");
                else hints.Add("'" + missing + "' is not installed or not on PATH (system_info lists the developer tools that were found).");
            }
            if (o.IndexOf("Python was not found; run without arguments to install from the Microsoft Store", StringComparison.OrdinalIgnoreCase) >= 0)
                hints.Add("Python is not installed (only the Microsoft Store shortcut exists). Try 'py'; otherwise tell the user Python needs to be installed.");
            Match bp = BadParam.Match(o);
            if (bp.Success && ps)
            {
                string lc = (cmd ?? "").Trim().ToLowerInvariant();
                if (Regex.IsMatch(lc, @"(^|[;|]\s*)(curl|wget)\s")) hints.Add("In Windows PowerShell 'curl' and 'wget' mean Invoke-WebRequest. Use curl.exe for the real curl, or Invoke-RestMethod / Invoke-WebRequest -OutFile.");
                else if (Regex.IsMatch(lc, @"(^|[;|]\s*)(rm|del|rmdir|rd)\s+-")) hints.Add("This is PowerShell, not bash. To delete files or folders, use the delete_file tool (it uses the Recycle Bin, so the user can undo it).");
                else if (Regex.IsMatch(lc, @"(^|[;|]\s*)(ls|dir)\s+-")) hints.Add("This is PowerShell, not bash: use Get-ChildItem -Force (add -Recurse for subfolders).");
                else if (Regex.IsMatch(lc, @"(^|[;|]\s*)(cp|copy)\s+-")) hints.Add("This is PowerShell, not bash: use Copy-Item -Recurse <from> <to>.");
                else if (Regex.IsMatch(lc, @"(^|[;|]\s*)(mv|move)\s+-")) hints.Add("This is PowerShell, not bash: use Move-Item -Force <from> <to>.");
                else if (Regex.IsMatch(lc, @"(^|[;|]\s*)(mkdir|md)\s+-p")) hints.Add("This is PowerShell: New-Item -ItemType Directory -Force <path> (it creates parent folders too).");
                else if (Regex.IsMatch(lc, @"(^|[;|]\s*)cat\s+-")) hints.Add("This is PowerShell: use Get-Content <file>.");
                else hints.Add("PowerShell does not know the parameter -" + bp.Groups[1].Value + "; this looks like bash syntax. Use the PowerShell form of the command.");
            }
            if (o.IndexOf("is not a valid statement separator", StringComparison.OrdinalIgnoreCase) >= 0)
                hints.Add("Windows PowerShell 5.1 has no && or ||. Separate commands with ';' and use 'if ($?) { ... }' to run the next one only after success.");
            if (o.IndexOf("cannot be loaded because running scripts is disabled", StringComparison.OrdinalIgnoreCase) >= 0)
                hints.Add("PowerShell blocks this .ps1 script. Use the .cmd version (for example npm.cmd, npx.cmd), or run: powershell -ExecutionPolicy Bypass -File <script.ps1>.");
            if (Regex.IsMatch(o, @"Device Guard|Application Control policy has blocked|Smart App Control|blocked by group policy|This program is blocked", RegexOptions.IgnoreCase))
                hints.Add("Windows blocked this program (Smart App Control or an application control policy). Do not retry it or look for a way around the block; tell the user which program Windows blocked.");
            else if (Regex.IsMatch(o, @"Access (to the path .{1,200} )?is denied|UnauthorizedAccessException|Permission denied|EACCES|EPERM", RegexOptions.IgnoreCase))
                hints.Add("This is a permission problem: the file may be read-only, open in another program, or need administrator rights. Do not try to get around it; tell the user if it is needed.");
            if (Regex.IsMatch(o, @"being used by another process|EBUSY|resource busy or locked", RegexOptions.IgnoreCase))
                hints.Add("Another program has the file open - often a program you started earlier is still running. Stop it (command_status with stop=true) and try again.");
            if (Regex.IsMatch(o, @"EADDRINUSE|address already in use|Only one usage of each socket address", RegexOptions.IgnoreCase))
                hints.Add("The port is already in use, probably by a server you started earlier. Stop it with command_status (stop=true) or use another port.");
            m = PyModule.Match(o);
            if (m.Success)
            {
                string mod = m.Groups[1].Value.Split('.')[0], pkg;
                if (!PipNames.TryGetValue(mod, out pkg)) pkg = mod;
                if (DocumentModule(mod)) hints.Add("The Python package " + pkg + " is not installed, and it isn't needed: " + BuiltInDocuments + " Use another tool only if the user asked for it or wants something that can't do, such as custom fonts, colors or an exact layout.");
                else hints.Add("The Python package " + pkg + " is not installed. Installing packages always needs the user's OK, also in Auto mode: if the task really needs it, run python -m pip install " + pkg + " with a description saying why (MinGent asks the user); otherwise solve it without the package.");
            }
            m = NpmScript.Match(o);
            if (m.Success) hints.Add("package.json has no \"" + m.Groups[1].Value + "\" script. Read package.json to see which scripts it has.");
            if (Regex.IsMatch(cmd ?? "", @"\\Microsoft\.NET\\Framework(64)?\\v4\.0\.30319\\csc(\.exe)?\b", RegexOptions.IgnoreCase) && Regex.IsMatch(o, @"\berror CS\d{4}\b"))
                hints.Add("This csc.exe knows only C# 5. If an error points at newer syntax, rewrite it in C# 5: string.Format or + instead of $\"...\", explicit null checks instead of ?. and ??=, \"Name\" instead of nameof, full bodies instead of => on methods and properties, a declared variable before out instead of out var, a small class instead of a tuple, and a separate method instead of a local function.");
            if (exitCode == -1073741515) hints.Add("The program could not start because a DLL it needs is missing (0xC0000135).");
            if (exitCode == 9009 && missing == null) hints.Add("cmd.exe could not find the program (exit code 9009). It is not installed or not on PATH.");
            if (timedOut)
            {
                string url = DetectUrl(o);
                if (url != null || Regex.IsMatch(o, @"listening|server (is )?(running|started)|ready in|compiled successfully|watching for", RegexOptions.IgnoreCase))
                    hints.Add("This is a server or watcher that keeps running. Start it with background=true, then check it with command_status" + (url != null ? " and open " + url + " with browser_navigate" : "") + ".");
            }
            if (hints.Count == 0) return null;
            return "Hint: " + string.Join(" ", hints.ToArray());
        }

        public static bool WaitingForInput(string tail)
        {
            if (string.IsNullOrEmpty(tail)) return false;
            string t = tail.TrimEnd();
            int nl = t.LastIndexOf('\n');
            string last = nl >= 0 ? t.Substring(nl + 1) : t;
            last = Shell.StripAnsi(last).Trim();
            if (last.Length == 0 || last.Length > 300) return false;
            return Prompt.IsMatch(last);
        }

        public static string DetectUrl(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;
            Match m = LocalUrl.Match(Shell.StripAnsi(output));
            if (!m.Success) return null;
            string u = m.Value.TrimEnd('.', ',', ';', ':');
            return Regex.Replace(u, @"^(https?://)(0\.0\.0\.0|\[::1?\])", "$1localhost", RegexOptions.IgnoreCase);
        }
    }

    static class PathHelp
    {
        public static List<string> Similar(string full, string workspace, CancelToken ct, bool folders)
        {
            List<string> res = new List<string>();
            if (string.IsNullOrEmpty(full)) return res;
            string name = System.IO.Path.GetFileName(full.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name)) return res;
            string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(name);
            List<string> roots = new List<string>();
            if (!string.IsNullOrEmpty(workspace) && Directory.Exists(workspace)) roots.Add(workspace);
            string anc = NearestExisting(System.IO.Path.GetDirectoryName(full));
            if (anc != null && (roots.Count == 0 || !AgentPaths.IsInside(anc, workspace)) && !Broad(anc)) roots.Add(anc);
            List<KeyValuePair<int, string>> scored = new List<KeyValuePair<int, string>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime until = DateTime.UtcNow.AddMilliseconds(1500);
            foreach (string root in roots)
            {
                ToolKit.Walk(root, delegate (string f)
                {
                    if (DateTime.UtcNow > until) return false;
                    string cand = folders ? System.IO.Path.GetDirectoryName(f) : f;
                    if (cand == null || !seen.Add(cand)) return true;
                    if (AgentPaths.Sensitive(cand) != null) return true;
                    int score = Score(System.IO.Path.GetFileName(cand), name, nameNoExt);
                    if (score >= 0) scored.Add(new KeyValuePair<int, string>(score, cand));
                    return true;
                }, ct, 40000);
            }
            scored.Sort(delegate (KeyValuePair<int, string> a, KeyValuePair<int, string> b)
            {
                if (a.Key != b.Key) return a.Key.CompareTo(b.Key);
                return a.Value.Length.CompareTo(b.Value.Length);
            });
            foreach (KeyValuePair<int, string> kv in scored)
            {
                if (res.Count >= 5) break;
                if (!string.Equals(kv.Value, full, StringComparison.OrdinalIgnoreCase)) res.Add(kv.Value);
            }
            return res;
        }

        public static int ExactNameMatches(List<string> found, string full)
        {
            string name = System.IO.Path.GetFileName(full ?? "");
            int n = 0;
            foreach (string f in found) if (string.Equals(System.IO.Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        static int Score(string cand, string name, string nameNoExt)
        {
            if (string.IsNullOrEmpty(cand)) return -1;
            if (string.Equals(cand, name, StringComparison.OrdinalIgnoreCase)) return 0;
            string cNoExt = System.IO.Path.GetFileNameWithoutExtension(cand);
            if (nameNoExt.Length >= 2 && string.Equals(cNoExt, nameNoExt, StringComparison.OrdinalIgnoreCase)) return 2;
            if (name.Length >= 4)
            {
                int d = Fuzzy.Distance(cand.ToLowerInvariant(), name.ToLowerInvariant(), 3);
                if (d <= 2) return 3 + d;
            }
            return -1;
        }

        static string NearestExisting(string dir)
        {
            string d = dir;
            for (int i = 0; i < 30 && !string.IsNullOrEmpty(d); i++)
            {
                try { if (Directory.Exists(d)) return d; } catch { return null; }
                d = System.IO.Path.GetDirectoryName(d);
            }
            return null;
        }

        static bool Broad(string dir)
        {
            string d = dir.TrimEnd('\\', '/');
            if (d.Length <= 3) return true;
            foreach (Environment.SpecialFolder f in new Environment.SpecialFolder[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.Windows })
            {
                try
                {
                    string p = Environment.GetFolderPath(f);
                    if (!string.IsNullOrEmpty(p) && (string.Equals(p.TrimEnd('\\', '/'), d, StringComparison.OrdinalIgnoreCase) || AgentPaths.IsInside(p, d))) return true;
                }
                catch { }
            }
            return d.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length < 3;
        }

        public static string Describe(List<string> found, string workspace)
        {
            if (found == null || found.Count == 0) return "";
            StringBuilder sb = new StringBuilder(" Did you mean: ");
            for (int i = 0; i < found.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ToolKit.Rel(found[i], workspace));
            }
            return sb.Append('?').ToString();
        }
    }

    static class StopWords
    {
        const string Lead = @"(?:^|[.!?;:,\n(]|\b(?:ok|okay|please|pls|plz|just|now|so|and|hey|wait|alright|then|you|to|guys|lol|bro|man)\b)\s*";
        const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex Whole = new Regex(@"^\W*(?:(?:ok|okay|pls|please|plz|wait|hey|no|just|alright|guys|lol)[\s,!.]+)*(?:stop|quit|halt|pause|cancel|abort|enough|hold\s+on|never\s*mind|nvm|forget\s+(?:it|about\s+it)|leave\s+it(?:\s+(?:there|for\s+now|as\s+is|at\s+that))?|that'?s\s+(?:enough|all)(?:\s+for\s+now)?|don'?t\s+(?:continue|bother))(?:[\s,]+(?:it|now|please|pls|plz|there|here|for\s+now|thanks|thank\s+you|thx|right\s+now|immediately))*\W*$", Opt);
        static readonly Regex[] Rules = {
            new Regex(Lead + @"(?:stop|quit|halt|cease|pause)\s+(?:working|work|everything|now|here|there|immediately|right\s+(?:now|there|here)|for\s+(?:now|today|the\s+day))\b", Opt),
            new Regex(Lead + @"(?:stop|quit|cease)\s+(?:working\s+|work\s+)?on\s+(?:this|that|it|everything|(?:the|this|that|my|our)\s+[\w-]+)\b", Opt),
            new Regex(Lead + @"(?:stop|quit|halt|pause|cancel|abort)\s+(?:it|this|that|everything|all\s+(?:of\s+)?(?:this|that|it))(?=\s*(?:$|[.!,;?]|now\b|please\b|pls\b|right\b|immediately\b|for\b|and\b))", Opt),
            new Regex(Lead + @"(?:stop|quit|halt|pause)\s+(?:what\s+you(?:'re|\s+are|re)\s+doing|doing\s+(?:this|that|it|anything))", Opt),
            new Regex(@"\bthat'?s\s+enough(?:\s+for\s+(?:now|today))?\s*(?:$|[.!,;])", Opt),
            new Regex(Lead + @"(?:don'?t|dont|do\s+not)\s+(?:continue|keep\s+going|go\s+on|proceed|do\s+anything\s+(?:else|more)|change\s+anything\s+(?:else|more)|make\s+(?:any\s+)?more\s+changes|touch\s+anything(?:\s+else)?)\b", Opt),
            new Regex(@"\bno\s+more\s+(?:changes|work|edits|fixes)\b", Opt)
        };
        static readonly Regex Conditional = new Regex(@"\b(?:if|when|whenever|unless|once|until|after|before|in\s+case)\b", Opt);
        static readonly Regex Review = new Regex(
            @"(?<!\b(?:don'?t|dont|do\s+not|not|never|no\s+need\s+to)\s+)\b(?:stop|cancel|kill|abort|skip|halt|quit)\s+(?:the\s+|that\s+|this\s+|your\s+|its\s+|it'?s\s+)?(?:independent\s+|automatic\s+|auto\s+)?review(?:er|ing)?\b" +
            @"(?!\s+(?:tables?|columns?|rows?|fields?|pages?|sections?|buttons?|forms?|screens?|steps?|tabs?|cards?|widgets?|features?|components?|modals?|dialogs?|links?|api|endpoints?|models?|scores?|counts?|text|data|queues?|lists?|comments?|requests?|stars?|ratings?|dates?)\b)" +
            @"|\b(?:don'?t|dont|do\s+not|no\s+need\s+to)\s+(?:run|start|do|bother\s+with)\s+(?:a|an|the|any)\s+(?:independent\s+|automatic\s+)?review\b" +
            @"|\bno\s+need\s+for\s+(?:a\s+|an\s+|the\s+)?(?:independent\s+|automatic\s+)?review\b" +
            @"|\bwithout\s+(?:a\s+|an\s+|the\s+)?(?:independent|automatic)\s+review\b" +
            @"|\b(?:turn|switch)\s+off\s+the\s+(?:independent\s+|automatic\s+)?review(?:er)?\b", Opt);

        public static bool SkipsReview(string text)
        {
            string t = (text ?? "").Trim();
            return t.Length > 0 && t.Length <= 2000 && Review.IsMatch(t);
        }
        static readonly Regex Work = new Regex(@"\b(?:add|fix|make|change|create|write|build|run|test|update|implement|set|remove|delete|deploy|install|open|use|try|rename|move|copy|put|edit|refactor|commit|push|merge|revert|undo|instead|then|button|shortcut|flag|option|setting|feature|function|method|step|limit)\b", Opt);

        public static bool Says(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0 || t.Length > 2000) return false;
            if (Whole.IsMatch(t)) return true;
            if (t.Length > 160 || Conditional.IsMatch(t)) return false;
            foreach (Regex r in Rules)
            {
                Match m = r.Match(t);
                if (!m.Success) continue;
                return !Work.IsMatch(t.Remove(m.Index, m.Length));
            }
            return false;
        }
    }

    class RunCoach
    {
        public int Nudges;
        int announce, todo, verify, refusal, summary;
        public bool AnyTool, Denied, FilesChanged, Reviewed, Listed;
        public string LastFamily = "";
        readonly List<string> unverified = new List<string>();
        public HashSet<string> Tools = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal);
        public bool Helper, WaitingOnUser;
        public string WrapReason;
        int refusals, unknown, edits;
        readonly Dictionary<string, Trail> trails = new Dictionary<string, Trail>(StringComparer.Ordinal);
        readonly HashSet<string> browserBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        class Trail
        {
            public int Fails, Blocks, Engines, EditsAt, Plain;
            public string Args = "", Error = "";
        }

        static readonly HashSet<string> CodeExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".py", ".pyw", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".cs", ".java", ".go", ".rs", ".c", ".cc", ".cpp", ".h", ".hpp", ".rb", ".php",
            ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".html", ".htm", ".css", ".scss", ".vue", ".svelte", ".kt", ".swift", ".lua", ".dart", ".scala",
            ".xaml", ".fs", ".vb", ".r", ".jl", ".zig", ".nim", ".ex", ".exs", ".erl", ".hs", ".clj", ".groovy", ".pl" };

        static readonly HashSet<string> Checks = new HashSet<string>(StringComparer.Ordinal) {
            "run_command", "command_status", "browser_navigate", "browser_read", "browser_screenshot", "browser_click", "browser_type", "browser_console",
            "app_read", "app_screenshot", "app_click", "app_type", "app_key", "app_scroll", "app_hover", "app_drag" };

        static readonly Regex Refusal = new Regex(
            @"\b(i (can't|cannot|can not|am unable to|am not able to|don't have (the )?(ability|access|permission)|do not have (the )?(ability|access|permission))|as an ai\b|i'm (just )?an? (ai|language model|text[- ]based)|i don't have (direct )?access)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex RefusalObject = new Regex(@"\b(file|files|folder|director|computer|pc|system|internet|web|brows|command|terminal|execut|run|access|disk|program|app|screen|your)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Announce = new Regex(
            @"^(now,?\s+|next,?\s+|first,?\s+|then,?\s+|okay,?\s+|ok,?\s+|alright,?\s+|great,?\s+)?(let me|let's|i'll|i will|i'm going to|i am going to|i'm now going to|i need to|i should|we need to|now i('ll| will| need to)|time to)\b(?!\s+know)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex ActionVerb = new Regex(
            @"\b(read|check|look|open|create|write|edit|update|fix|run|test|install|search|find|start|build|add|implement|modify|change|verify|try|fetch|navigate|click|make|set up|list|examine|inspect|analy[sz]e|review|investigate|launch|execute|compile|debug|refactor|generate|download|apply|replace|remove|delete|rename|move|copy|save|type|go|continue|proceed|explore|scan|view|load|call|use)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex WaitsForUser = new Regex(
            @"\b(once|after|when|as soon as|if|until) you('ve| have)?\b|\byou('ve| have) (completed|finished|solved|done)\b|\blet me know\b|\bwhen you're\b|\bonce that's done\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Untested = new Regex(
            @"\b(not|couldn't|could not|unable to|didn't|did not|can't|cannot|wasn't able to|haven't|have not|without)\s+(be\s+)?(test|run|verif|check|build|compil|execut|tried)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public bool PreferApp { get { return LastFamily == "app"; } }

        public int SiteBlocks(string key)
        {
            Trail t;
            return key != null && trails.TryGetValue(key, out t) ? t.Blocks : 0;
        }

        public static string KeyFor(string name, Dictionary<string, object> args)
        {
            if (name == "web_fetch")
            {
                string h = BlockCheck.HostOf(WebFetchTool.NormalizeUrl(Json.Str(args, "url")));
                return h != null ? "site:" + h : "web_fetch";
            }
            if (name == "run_command")
            {
                string cmd = Json.Str(args, "command") ?? "";
                if (CommandSafety.LooksNetwork(cmd))
                {
                    string h = BlockCheck.HostOf(BlockCheck.FirstUrl(cmd));
                    if (h != null) return "site:" + h;
                }
                return "cmd";
            }
            if (name == "browser_navigate")
            {
                string h = BlockCheck.HostOf(WebFetchTool.NormalizeUrl(Json.Str(args, "url")));
                return h != null ? "browser:" + h : "browser";
            }
            if (name == "web_search") return "search";
            return name;
        }

        static double Similar(string a, string b)
        {
            return Fuzzy.Similarity(Fuzzy.Tokens((a ?? "").ToLowerInvariant()), Fuzzy.Tokens((b ?? "").ToLowerInvariant()));
        }

        static string Tail(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(s.Length - n) : s; }

        string Routes(string host)
        {
            List<string> r = new List<string>();
            if (Helper)
            {
                r.Add("say in your report that " + (host ?? "the site") + " blocks automated access, and use other sources if you have them");
                return string.Join("; ", r.ToArray());
            }
            if (Tools.Contains("browser_navigate") && (host == null || !browserBlocked.Contains(host))) r.Add("open the page in the built-in browser with browser_navigate (it is a full browser, so it often gets through)");
            if (Tools.Contains("web_search")) r.Add("look for the same information on another site with web_search");
            r.Add("tell the user what you could not get");
            return JoinRoutes(r);
        }

        static string JoinRoutes(List<string> r)
        {
            if (r.Count > 1) r[r.Count - 1] = "or " + r[r.Count - 1];
            return string.Join("; ", r.ToArray());
        }

        string SearchRoutes()
        {
            if (Helper) return "say in your report that web search is not working right now, and work from what you already have";
            List<string> r = new List<string>();
            if (Tools.Contains("browser_navigate")) r.Add("search with another engine in the built-in browser with browser_navigate (for example https://search.brave.com/search?q=your+words) and read the results there");
            if (Tools.Contains("web_fetch")) r.Add("read a page whose address you already know with web_fetch");
            r.Add("tell the user");
            return JoinRoutes(r);
        }

        public string BeforeTool(string name, Dictionary<string, object> args)
        {
            Trail t;
            if (name == "web_fetch")
            {
                string key = KeyFor(name, args);
                if (key.StartsWith("site:", StringComparison.Ordinal) && trails.TryGetValue(key, out t) && t.Blocks >= 3)
                {
                    string host = key.Substring(5);
                    return "Not run: " + host + " answered the last " + t.Blocks + " requests from this chat with a bot check or a refusal, so MinGent did not send another one. Next: " + Routes(host) + ".";
                }
            }
            if (name == "web_search" && trails.TryGetValue("search", out t) && t.Engines >= 3)
                return "Not run: the search engines answered the last " + t.Engines + " searches with a bot check, an error or unrelated pages instead of results. Next: " + SearchRoutes() + ".";
            return null;
        }

        public string UnknownTool(string name)
        {
            unknown++;
            if (unknown >= 6 && WrapReason == null) WrapReason = "several calls used tool names that don't exist";
            if (unknown == 3) return "That's 3 calls to tools that don't exist in this chat. The tool names are listed in the error above; use one of them exactly as written.";
            return null;
        }

        public void CountRefusal()
        {
            refusals++;
            if (refusals >= 3 && WrapReason == null) WrapReason = "several calls were not run because they would only have repeated earlier results or requests";
        }

        public void AfterTool(string name, Dictionary<string, object> args, ToolOutcome o)
        {
            AnyTool = true;
            if (name == "todo_write" && o != null && !o.Error) Listed = true;
            if (name.StartsWith("app_", StringComparison.Ordinal)) LastFamily = "app";
            else if (name.StartsWith("browser_", StringComparison.Ordinal) || name == "watch_video") LastFamily = "browser";
            if (o == null) return;
            if (o.WaitsOnUser) WaitingOnUser = true;
            if ((o.WaitsOnUser || o.HostBlocked) && !string.IsNullOrEmpty(o.Host)) browserBlocked.Add(o.Host);
            if (o.Denied) { Denied = true; return; }
            if (!o.Refused && (Known.Count == 0 || Known.Contains(name))) Track(name, args, o);
            if (name == "delegate" && !o.Error && DelegateTool.KindArg(args) == "review") { Reviewed = true; unverified.Clear(); return; }
            if ((name == "write_file" || name == "edit_file" || name == "apply_patch") && !o.Error)
            {
                FilesChanged = true;
                edits++;
                List<string> paths = new List<string>();
                if (o.Paths != null) paths.AddRange(o.Paths);
                else
                {
                    string p = Json.Str(args, "path");
                    if (!string.IsNullOrEmpty(p)) paths.Add(p);
                }
                foreach (string p in paths)
                {
                    int dot = p.LastIndexOf('.'), cut = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
                    string ext = dot > cut ? p.Substring(dot).ToLowerInvariant() : "";
                    if (CodeExt.Contains(ext) && !unverified.Contains(p)) unverified.Add(p);
                }
                return;
            }
            if (Checks.Contains(name)) unverified.Clear();
        }

        void Track(string name, Dictionary<string, object> args, ToolOutcome o)
        {
            string key = KeyFor(name, args);
            bool site = key.StartsWith("site:", StringComparison.Ordinal);
            string host = !string.IsNullOrEmpty(o.Host) ? o.Host : site ? key.Substring(5) : null;
            string block = o.Block;
            if (block == "js")
            {
                o.AddNote("This page returned almost no text, which usually means it needs JavaScript to show its content. Next: " +
                          (Tools.Contains("browser_navigate") && !Helper ? "open it with browser_navigate." : "try another source for this information."));
                return;
            }
            bool failed = o.Error || o.Failed || block != null;
            Trail t;
            if (!trails.TryGetValue(key, out t)) { t = new Trail(); trails[key] = t; }
            if (!failed)
            {
                t.Fails = t.Blocks = t.Engines = t.Plain = 0;
                t.Args = t.Error = "";
                return;
            }
            string argsText = name == "web_search" ? (Json.Str(args, "query") ?? "") : Json.Write(args ?? new Dictionary<string, object>());
            string errText = Tail(o.Text, 600);
            bool same;
            if (t.Fails == 0) same = false;
            else if (site || key.StartsWith("browser:", StringComparison.Ordinal)) same = true;
            else if (key == "search") same = block == "engines" || Similar(argsText, t.Args) >= 0.5;
            else same = Similar(argsText, t.Args) >= 0.8 && Similar(errText, t.Error) >= 0.6;
            if (!same && t.Fails > 0) { t.Fails = 0; t.Blocks = 0; t.Engines = 0; t.Plain = 0; }
            bool editedSince = t.Fails > 0 && edits > t.EditsAt;
            t.Fails++;
            t.Plain = editedSince ? 1 : t.Plain + 1;
            if (block == "bot" || block == "refused" || block == "limited") t.Blocks++; else t.Blocks = 0;
            if (block == "engines") t.Engines++;
            t.Args = argsText;
            t.Error = errText;
            t.EditsAt = edits;
            string first = FirstNote(block, host, o.Status);
            if (t.Fails == 1) { o.AddNote(first); return; }
            if (t.Plain >= 5 && WrapReason == null) WrapReason = "the same kind of call failed " + t.Plain + " times in a row with nothing changed in between";
            if (t.Fails > 2 && first == null) return;
            if (site)
                o.AddNote("That's " + t.Fails + " requests to " + host + " in a row that got " + Reason(block, o.Status) + ". Next: " + Routes(host) + ".");
            else if (key == "search")
                o.AddNote(block == "engines"
                    ? "That's " + t.Fails + " searches in a row that the search engines refused or answered with unrelated pages. Next: " + SearchRoutes() + "."
                    : "That's " + t.Fails + " similar searches in a row with no results. Next: a different approach, such as " + (Helper ? "other sources you know of, or say what you could not find." : SearchRoutes() + "."));
            else if (key.StartsWith("browser:", StringComparison.Ordinal))
                o.AddNote(first ?? "That's " + t.Fails + " browser calls on " + (host ?? "this site") + " in a row that failed. Next: read the error, then try a different element or page, or tell the user.");
            else if (editedSince)
                o.AddNote("This failed with the same error as before your last change, so that change did not fix it. Next: read the error again and look for a different cause.");
            else
                o.AddNote("This failed with the same error as your previous, nearly identical call. Next: read the error and change something that matters (path, name, command or approach), or tell the user what is in the way.");
        }

        static string Reason(string block, int status)
        {
            switch (block)
            {
                case "bot": return "a bot check" + (status > 0 ? " (HTTP " + status + ")" : "");
                case "refused": return "a refusal (HTTP 403)";
                case "limited": return "\"too many requests\" (HTTP 429)";
                case "notfound": return "\"not found\" (HTTP " + (status > 0 ? status : 404) + ")";
            }
            return "an error";
        }

        string FirstNote(string block, string host, int status)
        {
            string h = host ?? "The site";
            switch (block)
            {
                case "bot":
                    return h + " answered with a bot-protection check" + (status > 0 ? " (HTTP " + status + ")" : "") + " instead of the page. Requests from scripts on this PC, such as curl or Invoke-WebRequest, usually get the same answer. Next: " + Routes(host) + ".";
                case "refused":
                    return h + " refused the request (HTTP 403 Forbidden); it may not accept requests from scripts. Next: " + Routes(host) + ".";
                case "limited":
                    return h + " answered HTTP 429 (too many requests). Next: wait a while before asking it again, or use another source.";
                case "notfound":
                    return "That address does not exist (HTTP " + (status > 0 ? status : 404) + "). Next: " + (Tools.Contains("web_search") ? "find the right address with web_search instead of guessing." : "check the address.");
                case "engines":
                    return "The search engines answered with a bot check, an error or pages unrelated to the query instead of results, so rewording the query will not change the answer. Next: " + SearchRoutes() + ".";
                case "empty":
                    return "No results. Next: try once with fewer or broader words; if that finds nothing too, use another approach.";
                case "offline":
                    return "The search engines could not be reached, so this PC may be offline. Next: continue without searching, or tell the user.";
            }
            return null;
        }

        public string Told;

        public string EndOfTurn(string text, List<TodoItem> todos)
        {
            string t = (text ?? "").Trim();
            Told = null;
            if (Nudges >= 4) return null;
            bool question = EndsWithQuestion(t);
            if (!AnyTool && refusal == 0 && t.Length > 0 && t.Length < 2500)
            {
                foreach (string sentence in Regex.Split(t, @"(?<=[.!?\n])\s+"))
                {
                    if (Refusal.IsMatch(sentence) && RefusalObject.IsMatch(sentence))
                    {
                        refusal++;
                        Nudges++;
                        Told = "The agent said it can't do this itself, so MinGent reminded it of its tools.";
                        return "[MinGent] You can do this yourself. In this app you have tools to read and write files, run commands on this PC, search the web and use a browser, and the app asks the user for permission when it is needed. Use the tools to do what the user asked.";
                    }
                }
            }
            if (!question && !WaitingOnUser && announce < 2 && Announces(t))
            {
                announce++;
                Nudges++;
                Told = "The agent said what it would do next but stopped, so MinGent asked it to carry on.";
                return "[MinGent] Your message says you are about to do something, but you ended your turn without calling a tool. If you meant to continue, make the tool call now. If you are finished, give your final answer.";
            }
            if (!question && !WaitingOnUser && todo == 0 && todos != null)
            {
                List<string> open = new List<string>();
                foreach (TodoItem it in todos) if (it.Status != "completed") open.Add(it.Content);
                if (open.Count > 0)
                {
                    todo++;
                    Nudges++;
                    Told = "The checklist still has open items, so MinGent asked the agent to carry on.";
                    StringBuilder sb = new StringBuilder("[MinGent] Your checklist still has unfinished items:\n");
                    foreach (string s in open) sb.Append("- ").Append(s).Append('\n');
                    sb.Append("Continue working on them. If an item is no longer needed or cannot be done, update the checklist with todo_write and explain why in your answer.");
                    return sb.ToString();
                }
            }
            if (!question && verify == 0 && unverified.Count > 0 && !Denied && !Untested.IsMatch(t))
            {
                verify++;
                Nudges++;
                Told = "MinGent asked the agent to test its changes before it finishes.";
                List<string> names = new List<string>();
                foreach (string p in unverified) { if (names.Count < 5) names.Add(System.IO.Path.GetFileName(p)); }
                return "[MinGent] You changed code (" + string.Join(", ", names.ToArray()) + ") but did not run or test it after the last change. If it can be checked here, check it now the way a person would - run it, run its tests or build it, or open it in the browser - and fix any problems you find. If it cannot be checked here, say so in your final answer.";
            }
            if (!question && summary == 0 && FilesChanged && t.Length < 30)
            {
                summary++;
                Nudges++;
                Told = "MinGent asked the agent to sum up what it did.";
                return "[MinGent] Give the user a short final summary: what you changed (with file paths), how you checked it, and anything they still need to do.";
            }
            return null;
        }

        public static bool IsQuestion(string t) { return EndsWithQuestion((t ?? "").Trim()); }

        static bool EndsWithQuestion(string t)
        {
            if (t.EndsWith("?")) return true;
            int nl = t.LastIndexOf('\n');
            string last = nl >= 0 ? t.Substring(nl + 1).Trim() : t;
            return last.EndsWith("?") || last.EndsWith("?**") || last.EndsWith("?*");
        }

        static bool Announces(string t)
        {
            if (t.Length == 0) return false;
            string tail = t.Length > 400 ? t.Substring(t.Length - 400) : t;
            if (tail.EndsWith(":") && !tail.EndsWith("::")) return true;
            string[] sentences = Regex.Split(tail, @"(?<=[.!])\s+|\n+");
            for (int i = sentences.Length - 1; i >= 0; i--)
            {
                string s = sentences[i].Trim().TrimStart('*', '-', '>', ' ');
                if (s.Length == 0) continue;
                if (WaitsForUser.IsMatch(s)) return false;
                return Announce.IsMatch(s) && ActionVerb.IsMatch(s);
            }
            return false;
        }
    }

    class RepeatGuard
    {
        class Entry
        {
            public int World, Same;
            public string Hash;
            public long Stamp;
        }

        readonly Dictionary<string, Entry> seen = new Dictionary<string, Entry>(StringComparer.Ordinal);
        public int World;

        static readonly HashSet<string> Reads = new HashSet<string>(StringComparer.Ordinal) {
            "read_file", "list_directory", "find_files", "search_text", "web_fetch", "web_search", "system_info", "analyze_audio" };
        static readonly HashSet<string> Live = new HashSet<string>(StringComparer.Ordinal) {
            "command_status", "browser_read", "browser_screenshot", "watch_video", "browser_console", "app_read", "app_screenshot" };
        static readonly HashSet<string> Inert = new HashSet<string>(StringComparer.Ordinal) { "todo_write", "open_in_editor" };
        static readonly HashSet<string> AppActions = new HashSet<string>(StringComparer.Ordinal) { "app_click", "app_type", "app_key", "app_scroll", "app_hover", "app_drag" };
        static readonly Regex ScriptName = new Regex(@"cmd-[0-9a-f]{12}\.ps1", RegexOptions.CultureInvariant);

        public static bool Changes(string name, Dictionary<string, object> args)
        {
            if (name == "browser_console") return (Json.Str(args, "code") ?? "").Trim().Length > 0;
            if (Reads.Contains(name) || Live.Contains(name) || Inert.Contains(name)) return false;
            if (name == "memory") { string a = (Json.Str(args, "action") ?? "").ToLowerInvariant(); return a != "list" && a != "read"; }
            return true;
        }

        static long Stamp()
        {
            long s = 0;
            try
            {
                foreach (ShellJob j in Shell.Background()) s += 1000003L * j.Id + j.Length;
            }
            catch { }
            return s;
        }

        public string Check(string sig, string name)
        {
            if (!Reads.Contains(name)) return null;
            Entry e;
            if (!seen.TryGetValue(sig, out e) || e.World != World || e.Same < 1 || e.Stamp != Stamp()) return null;
            return "Not run: you already made this exact " + name + " call twice since anything last changed, and it returned the same result both times. Next: work with that result, or try different arguments or another tool.";
        }

        public string After(string sig, string name, Dictionary<string, object> args, ToolOutcome o, int worldBefore)
        {
            if (o == null || o.Refused) return null;
            if (o.Denied) return null;
            if (Changes(name, args)) World++;
            string h = Hash(o);
            long stamp = Stamp();
            Entry prev;
            int same = 0;
            bool still = false;
            if (seen.TryGetValue(sig, out prev) && prev.World == worldBefore && prev.Hash == h)
            {
                same = prev.Same + 1;
                still = prev.Stamp == stamp;
            }
            Entry e = new Entry();
            e.World = World;
            e.Hash = h;
            e.Same = still ? same : 0;
            e.Stamp = stamp;
            seen[sig] = e;
            if (same == 0) return null;
            if (Reads.Contains(name))
                return same == 1 && still ? "Same result as your previous identical call; nothing has changed since then." : null;
            if (same < 2) return null;
            string extra = name == "command_status" ? " If the command is still working, give it more time between checks (wait_seconds can be up to 60)."
                         : Live.Contains(name) ? " Nothing on the page or in the program has changed in between."
                         : AppActions.Contains(name) ? " The window's controls read the same each time. If the program draws its own picture, like a game, check what changed with app_screenshot." : "";
            return "This returned exactly the same result as your previous " + same + " identical calls." + extra;
        }

        static string Hash(ToolOutcome o)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(o.Error ? '1' : '0').Append('|');
            sb.Append(ScriptName.Replace(o.Text ?? "", "cmd.ps1"));
            if (o.Images != null) foreach (ImageData img in o.Images) sb.Append('|').Append(img.Base64.Length).Append(':').Append(Fnv(img.Base64));
            return sb.Length.ToString(CultureInfo.InvariantCulture) + ":" + Fnv(sb.ToString());
        }

        static string Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    class ApplyPatchTool : AgentTool
    {
        public override string Name { get { return "apply_patch"; } }
        public override string Description { get { return "Apply a patch in the \"*** Begin Patch\" format or as a unified diff (--- a/file, +++ b/file, @@ hunks) to files in the workspace."; } }
        public override Dictionary<string, object> Schema { get { return Obj("input", P("string", "The patch text"), true); } }
        public override string Label(Dictionary<string, object> a) { return "Apply a patch"; }

        class FileOp
        {
            public string Kind = "update", Path, MoveTo;
            public List<Hunk> Hunks = new List<Hunk>();
            public List<string> AddLines = new List<string>();
        }

        class Hunk
        {
            public string Header = "";
            public List<string> Old = new List<string>(), New = new List<string>();
        }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string text = S(a, "input");
            if (string.IsNullOrEmpty(text)) foreach (KeyValuePair<string, object> kv in a) { string s = kv.Value as string; if (s != null && s.Length > (text ?? "").Length) text = s; }
            if (string.IsNullOrEmpty(text)) return ToolOutcome.Fail("input is required: the patch text");
            text = Document.NormalizeNewlines(text);
            string perr;
            List<FileOp> ops = text.IndexOf("*** Begin Patch", StringComparison.Ordinal) >= 0 || text.IndexOf("*** Update File:", StringComparison.Ordinal) >= 0 || text.IndexOf("*** Add File:", StringComparison.Ordinal) >= 0
                ? ParseV4A(text, out perr) : ParseUnified(text, out perr);
            if (ops == null) return ToolOutcome.Fail(perr);
            if (ops.Count == 0) return ToolOutcome.Fail("the patch contains no file changes");
            List<KeyValuePair<FileTarget, string>> writes = new List<KeyValuePair<FileTarget, string>>();
            List<string> deletes = new List<string>(), report = new List<string>(), paths = new List<string>(), warnings = new List<string>();
            foreach (FileOp op in ops)
            {
                string err;
                FileTarget t = ToolKit.Resolve(ctx, op.Path, true, out err);
                if (t == null) return ToolOutcome.Fail(op.Path + ": " + err);
                string target = t.Tab != null ? (t.Tab.Path ?? t.Tab.Title) : t.Full;
                if (op.Kind == "delete")
                {
                    if (t.Full == null || !File.Exists(t.Full)) return ToolOutcome.Fail("cannot delete " + op.Path + ": file not found");
                    string w = ctx.CheckWrite(t.Full, "Delete a file") ?? AskDelete(ctx, t.Full);
                    if (w != null) { ToolOutcome d = ToolOutcome.Fail(w); d.Denied = true; return d; }
                    deletes.Add(t.Full);
                    report.Add("deleted " + ToolKit.Rel(t.Full, ctx.Workspace));
                    continue;
                }
                if (op.Kind == "add")
                {
                    if (t.Full != null && DocText.IsDocument(t.Full)) return ToolOutcome.Fail(DocText.CannotWrite(t.Full));
                    if (t.Tab == null && t.Full != null && File.Exists(t.Full)) return ToolOutcome.Fail("cannot add " + op.Path + ": the file already exists (use an Update File section)");
                    string wPath = t.Tab != null ? t.Tab.Path : t.Full;
                    if (wPath != null) { string w = ctx.CheckWrite(wPath, "Create a file"); if (w != null) { ToolOutcome d = ToolOutcome.Fail(w); d.Denied = true; return d; } }
                    string content = string.Join("\n", op.AddLines.ToArray());
                    if (op.AddLines.Count > 0) content += "\n";
                    writes.Add(new KeyValuePair<FileTarget, string>(t, content));
                    report.Add("created " + ToolKit.Rel(target, ctx.Workspace));
                    paths.Add(target);
                    string pr = WriteCheck.Problems(target, null, content, false);
                    if (pr != null) warnings.Add(System.IO.Path.GetFileName(target) + ": " + pr);
                    continue;
                }
                if (t.Full != null && DocText.IsDocument(t.Full)) return ToolOutcome.Fail(DocText.CannotWrite(t.Full));
                string before;
                if (t.Tab != null) before = Document.NormalizeNewlines(t.Tab.Text ?? "");
                else
                {
                    if (!File.Exists(t.Full)) return ToolOutcome.Fail("cannot update " + op.Path + ": file not found" + PathHelp.Describe(PathHelp.Similar(t.Full, ctx.Workspace, ctx.Ct, false), ctx.Workspace));
                    int enc; string eol;
                    before = ToolKit.ReadDisk(t.Full, out enc, out eol, out err);
                    if (before == null) return ToolOutcome.Fail(op.Path + ": " + err);
                }
                string path0 = t.Tab != null ? t.Tab.Path : t.Full;
                if (path0 != null) { string w = ctx.CheckWrite(path0, "Change a file"); if (w != null) { ToolOutcome d = ToolOutcome.Fail(w); d.Denied = true; return d; } }
                string after = before;
                int hn = 0;
                List<string> lines = new List<string>(after.Split('\n'));
                int cursor = 0;
                foreach (Hunk h in op.Hunks)
                {
                    hn++;
                    string herr;
                    if (!ApplyHunk(lines, h, ref cursor, out herr))
                        return ToolOutcome.Fail("hunk " + hn + " for " + op.Path + " did not match the file: " + herr + " Nothing was changed. Read the file and send a patch whose context lines match it exactly.");
                }
                after = string.Join("\n", lines.ToArray());
                if (op.MoveTo != null)
                {
                    FileTarget mt = ToolKit.Resolve(ctx, op.MoveTo, false, out err);
                    if (mt == null || mt.Full == null) return ToolOutcome.Fail(op.MoveTo + ": " + (err ?? "cannot move there"));
                    string w = ctx.CheckWrite(mt.Full, "Create a file") ?? (t.Full != null ? AskDelete(ctx, t.Full) : null);
                    if (w != null) { ToolOutcome d = ToolOutcome.Fail(w); d.Denied = true; return d; }
                    writes.Add(new KeyValuePair<FileTarget, string>(mt, after));
                    if (t.Full != null) deletes.Add(t.Full);
                    report.Add("moved " + ToolKit.Rel(target, ctx.Workspace) + " to " + ToolKit.Rel(mt.Full, ctx.Workspace) + " (" + hn + (hn == 1 ? " hunk)" : " hunks)"));
                    paths.Add(mt.Full);
                }
                else
                {
                    writes.Add(new KeyValuePair<FileTarget, string>(t, after));
                    report.Add("updated " + ToolKit.Rel(target, ctx.Workspace) + " (" + hn + (hn == 1 ? " hunk)" : " hunks)"));
                    paths.Add(target);
                }
                string pr2 = WriteCheck.Problems(target, before, after, false);
                if (pr2 != null) warnings.Add(System.IO.Path.GetFileName(target) + ": " + pr2);
            }
            foreach (KeyValuePair<FileTarget, string> w in writes)
            {
                FileTarget t = w.Key;
                if (t.Tab != null)
                {
                    string e2 = ctx.Editor.SetTabText(t.Tab.Key, t.Tab.Version, w.Value, ctx.Cfg.AutoSave, ctx.Cfg.RunId);
                    if (e2 != null && e2.StartsWith("!")) { warnings.Add(e2.Substring(1)); e2 = null; }
                    if (e2 != null) return ToolOutcome.Fail(e2);
                    continue;
                }
                int enc = 0;
                string eol = "\n", rerr;
                if (File.Exists(t.Full)) ToolKit.ReadDisk(t.Full, out enc, out eol, out rerr);
                string werr = ToolKit.WriteDisk(ctx, t.Full, w.Value, enc, eol);
                if (werr != null) return ToolOutcome.Fail("could not write " + t.Full + ": " + werr);
                ctx.Session.ReadFiles.Add(t.Full);
            }
            List<string> kept = new List<string>();
            foreach (string d in deletes)
            {
                bool fresh = !ctx.Changes.Recorded(d);
                ctx.Changes.Before(d);
                if (!RecycleBin.Send(d))
                {
                    if (fresh) ctx.Changes.Forget(d);
                    kept.Add(d);
                    continue;
                }
                ctx.Editor.FileChangedOnDisk(d);
            }
            if (kept.Count > 0)
                return ToolOutcome.Fail((writes.Count > 0 ? "The other changes in the patch were made, but " : "") + "Windows did not delete " + string.Join(", ", kept.ToArray()) + ". A program may have it open. Close it, or stop the command that uses it, then delete it with delete_file.");
            ToolOutcome o = ToolOutcome.Ok("Applied the patch: " + string.Join("; ", report.ToArray()) + "." + (warnings.Count > 0 ? "\n" + string.Join("\n", warnings.ToArray()) : ""), report.Count == 1 ? report[0] : report.Count + " files");
            o.Paths = paths;
            return o;
        }

        static string AskDelete(ToolContext ctx, string full)
        {
            PermissionState ps = ctx.Session.Perms;
            if (!ps.Auto || ps.WebGuard && ps.Tainted) return null;
            string real = AgentPaths.Real(full);
            bool through = !string.Equals(real, full, StringComparison.OrdinalIgnoreCase);
            if (AgentPaths.IsInside(full, ctx.Workspace) && (!through || AgentPaths.IsInside(real, AgentPaths.Real(ctx.Workspace)))) return null;
            PermissionAsk ask = new PermissionAsk();
            ask.Kind = "write";
            ask.Category = ctx.Guarded(PermissionCategory.Delete);
            ask.Title = "Delete a file outside the work folder";
            ask.High = true;
            ask.Detail = full + (through ? " (really " + real + ")" : "");
            ask.Key = "delete:" + full;
            ask.Reason = "It goes to the Recycle Bin.";
            if (!ctx.Approve(ask)) return "The user did not allow deleting " + full + ". Do not try another way; ask the user if you need it.";
            return null;
        }

        static bool ApplyHunk(List<string> lines, Hunk h, ref int cursor, out string err)
        {
            err = null;
            int start = cursor;
            if (h.Header.Trim().Length > 0)
            {
                string hd = h.Header.Trim();
                for (int i = cursor; i < lines.Count; i++) if (lines[i].Trim() == hd || (hd.Length > 8 && lines[i].Contains(hd))) { start = i; break; }
            }
            if (h.Old.Count == 0)
            {
                int at = lines.Count;
                if (at > 0 && lines[at - 1].Length == 0) at--;
                lines.InsertRange(at, h.New);
                cursor = at + h.New.Count;
                return true;
            }
            int found = Find(lines, h.Old, start, false);
            if (found < 0) found = Find(lines, h.Old, 0, false);
            if (found < 0) found = Find(lines, h.Old, start, true);
            if (found < 0) found = Find(lines, h.Old, 0, true);
            if (found < 0)
            {
                string first = "";
                foreach (string l in h.Old) if (l.Trim().Length > 0) { first = l.Trim(); break; }
                err = "these lines were not found: \"" + (first.Length > 100 ? first.Substring(0, 100) + "..." : first) + "\"" + (h.Old.Count > 1 ? " (and " + (h.Old.Count - 1) + " more)." : ".");
                return false;
            }
            List<string> repl = new List<string>();
            for (int k = 0; k < h.New.Count; k++) repl.Add(h.New[k]);
            lines.RemoveRange(found, h.Old.Count);
            lines.InsertRange(found, repl);
            cursor = found + repl.Count;
            return true;
        }

        static int Find(List<string> lines, List<string> want, int from, bool loose)
        {
            for (int i = Math.Max(0, from); i + want.Count <= lines.Count; i++)
            {
                bool ok = true;
                for (int k = 0; k < want.Count && ok; k++)
                {
                    string a = lines[i + k], b = want[k];
                    ok = loose ? a.Trim() == b.Trim() : a.TrimEnd() == b.TrimEnd();
                }
                if (ok) return i;
            }
            return -1;
        }

        static List<FileOp> ParseV4A(string text, out string err)
        {
            err = null;
            List<FileOp> ops = new List<FileOp>();
            FileOp cur = null;
            Hunk h = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw;
                if (line.StartsWith("*** Begin Patch") || line.StartsWith("*** End Patch") || line.StartsWith("*** End of File")) { h = null; continue; }
                if (line.StartsWith("*** Update File:")) { cur = new FileOp(); cur.Path = line.Substring(16).Trim(); ops.Add(cur); h = null; continue; }
                if (line.StartsWith("*** Add File:")) { cur = new FileOp(); cur.Kind = "add"; cur.Path = line.Substring(13).Trim(); ops.Add(cur); h = null; continue; }
                if (line.StartsWith("*** Delete File:")) { cur = new FileOp(); cur.Kind = "delete"; cur.Path = line.Substring(16).Trim(); ops.Add(cur); h = null; continue; }
                if (line.StartsWith("*** Move to:")) { if (cur != null) cur.MoveTo = line.Substring(12).Trim(); continue; }
                if (cur == null) continue;
                if (cur.Kind == "add") { cur.AddLines.Add(line.StartsWith("+") ? line.Substring(1) : line); continue; }
                if (cur.Kind == "delete") continue;
                if (line.StartsWith("@@"))
                {
                    h = new Hunk();
                    h.Header = line.Substring(2).Trim();
                    if (h.Header.EndsWith("@@")) h.Header = "";
                    cur.Hunks.Add(h);
                    continue;
                }
                if (h == null) { h = new Hunk(); cur.Hunks.Add(h); }
                AddHunkLine(h, line);
            }
            foreach (FileOp op in ops)
            {
                if (op.Kind == "add" && op.AddLines.Count > 0 && op.AddLines[op.AddLines.Count - 1].Length == 0) op.AddLines.RemoveAt(op.AddLines.Count - 1);
                foreach (Hunk x in op.Hunks) Trim(x);
                op.Hunks.RemoveAll(delegate (Hunk x) { return x.Old.Count == 0 && x.New.Count == 0; });
                if (op.Kind == "update" && op.Hunks.Count == 0 && op.MoveTo == null) { err = "the patch for " + op.Path + " has no changes"; return null; }
            }
            return ops;
        }

        static List<FileOp> ParseUnified(string text, out string err)
        {
            err = null;
            List<FileOp> ops = new List<FileOp>();
            FileOp cur = null;
            Hunk h = null;
            string pendingOld = null;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("diff --git") || line.StartsWith("index ") || line.StartsWith("new file mode") || line.StartsWith("deleted file mode") || line.StartsWith("similarity index")) continue;
                if (line.StartsWith("--- ") && i + 1 < lines.Length && lines[i + 1].StartsWith("+++ "))
                {
                    pendingOld = StripPrefix(line.Substring(4));
                    string nw = StripPrefix(lines[i + 1].Substring(4));
                    i++;
                    cur = new FileOp();
                    if (nw == "/dev/null") { cur.Kind = "delete"; cur.Path = pendingOld; }
                    else if (pendingOld == "/dev/null") { cur.Kind = "add"; cur.Path = nw; }
                    else { cur.Path = pendingOld; if (!string.Equals(nw, pendingOld, StringComparison.OrdinalIgnoreCase)) cur.MoveTo = nw; }
                    ops.Add(cur);
                    h = null;
                    continue;
                }
                if (cur == null) continue;
                if (line.StartsWith("@@"))
                {
                    h = new Hunk();
                    Match m = Regex.Match(line, @"^@@[^@]*@@\s?(.*)$");
                    h.Header = m.Success ? m.Groups[1].Value.Trim() : "";
                    cur.Hunks.Add(h);
                    continue;
                }
                if (line.StartsWith("\\ No newline")) continue;
                if (cur.Kind == "add") { if (line.StartsWith("+")) cur.AddLines.Add(line.Substring(1)); continue; }
                if (cur.Kind == "delete" || h == null) continue;
                AddHunkLine(h, line);
            }
            if (ops.Count == 0) { err = "this is not a patch: use the \"*** Begin Patch\" format or a unified diff with ---/+++ lines and @@ hunks"; return null; }
            foreach (FileOp op in ops)
            {
                foreach (Hunk x in op.Hunks) Trim(x);
                op.Hunks.RemoveAll(delegate (Hunk x) { return x.Old.Count == 0 && x.New.Count == 0; });
            }
            return ops;
        }

        static string StripPrefix(string p)
        {
            string s = p.Trim();
            int tab = s.IndexOf('\t');
            if (tab > 0) s = s.Substring(0, tab);
            if (s.StartsWith("a/") || s.StartsWith("b/")) s = s.Substring(2);
            return s;
        }

        static void AddHunkLine(Hunk h, string line)
        {
            if (line.StartsWith("+")) h.New.Add(line.Substring(1));
            else if (line.StartsWith("-")) h.Old.Add(line.Substring(1));
            else
            {
                string c = line.StartsWith(" ") ? line.Substring(1) : line;
                h.Old.Add(c);
                h.New.Add(c);
            }
        }

        static void Trim(Hunk h)
        {
            while (h.Old.Count > 0 && h.New.Count > 0 && h.Old[h.Old.Count - 1].Length == 0 && h.New[h.New.Count - 1].Length == 0)
            {
                h.Old.RemoveAt(h.Old.Count - 1);
                h.New.RemoveAt(h.New.Count - 1);
            }
        }
    }
}
