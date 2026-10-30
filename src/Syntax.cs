using System;
using System.Collections.Generic;
using System.IO;

namespace MinGent
{
    class Lang
    {
        public string Name;
        public string Exts = "";
        public string KwText = "", TypeText = "", ConstText = "";
        public HashSet<string> Kw, Types, Consts;
        public bool IgnoreCase;
        public string[] LineCmt = Syntax.None;
        public string[] BlockOpen = Syntax.None, BlockClose = Syntax.None;
        public string[] MlOpen = Syntax.None, MlClose = Syntax.None;
        public bool[] MlEsc = Syntax.NoB, MlDouble = Syntax.NoB;
        public string Quotes = "";
        public char Esc = '\\';
        public bool HashPre, AtPre, ShortSingle, DashWords, VarColon, VarSpecial, VarParen;
        public bool HashColors, PercentUnits, BangMacros, HashAttr, ColonSymbols, Func = true;
        public int ColonKeys;
        public string VarPrefix = "", IdentExtra = "";
        public int Mode;
        public bool EmbedScripts, EmbedPhp;
    }

    static class Syntax
    {
        public static readonly string[] None = new string[0];
        public static readonly bool[] NoB = new bool[0];
        public const int MaxLineLen = 20000;
        public static readonly List<Lang> All = new List<Lang>();
        static readonly Dictionary<string, Lang> byKey = new Dictionary<string, Lang>(StringComparer.OrdinalIgnoreCase);
        public static Lang Plain, JS, CSS, PhpCode;

        static Syntax()
        {
            Define();
            foreach (Lang l in All) Init(l);
            Init(PhpCode);
            foreach (Lang l in All)
                foreach (string k in l.Exts.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    byKey[k] = l;
            All.Sort(delegate(Lang a, Lang b)
            {
                if (a == Plain) return -1;
                if (b == Plain) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        static void Init(Lang l)
        {
            l.Kw = Set(l.KwText, l.IgnoreCase);
            l.Types = Set(l.TypeText, l.IgnoreCase);
            l.Consts = Set(l.ConstText, l.IgnoreCase);
        }

        static HashSet<string> Set(string words, bool ic)
        {
            HashSet<string> h = new HashSet<string>(ic ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string w in words.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) h.Add(w);
            return h;
        }

        public static Lang ByName(string name)
        {
            foreach (Lang l in All) if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        public static Lang Detect(string path, string firstLine)
        {
            Lang l;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string name = Path.GetFileName(path);
                    if (byKey.TryGetValue(name, out l)) return l;
                    string ext = Path.GetExtension(name);
                    if (!string.IsNullOrEmpty(ext) && byKey.TryGetValue(ext, out l)) return l;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(firstLine))
            {
                string f = firstLine.TrimStart();
                if (f.StartsWith("#!"))
                {
                    if (f.Contains("python")) return ByName("Python");
                    if (f.Contains("node") || f.Contains("deno")) return ByName("JavaScript");
                    if (f.Contains("perl")) return ByName("Perl");
                    if (f.Contains("ruby")) return ByName("Ruby");
                    if (f.Contains("pwsh") || f.Contains("powershell")) return ByName("PowerShell");
                    if (f.Contains("php")) return ByName("PHP");
                    if (f.Contains("lua")) return ByName("Lua");
                    if (f.Contains("sh")) return ByName("Shell");
                }
                if (f.StartsWith("<?xml")) return ByName("XML");
                if (f.StartsWith("<?php")) return ByName("PHP");
                if (f.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || f.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                    return ByName("HTML");
            }
            return Plain;
        }

        static Lang Add(string name, string exts)
        {
            Lang l = new Lang();
            l.Name = name;
            l.Exts = exts;
            All.Add(l);
            return l;
        }

        static T[] Append<T>(T[] arr, T v)
        {
            T[] r = new T[arr.Length + 1];
            Array.Copy(arr, r, arr.Length);
            r[arr.Length] = v;
            return r;
        }

        static void Block(Lang l, string open, string close)
        {
            l.BlockOpen = Append(l.BlockOpen, open);
            l.BlockClose = Append(l.BlockClose, close);
        }

        static void Ml(Lang l, string open, string close, bool esc, bool dbl)
        {
            l.MlOpen = Append(l.MlOpen, open);
            l.MlClose = Append(l.MlClose, close);
            l.MlEsc = Append(l.MlEsc, esc);
            l.MlDouble = Append(l.MlDouble, dbl);
        }

        static string[] A(params string[] x) { return x; }

        static Lang CLike(string name, string exts, string kw, string types, string consts)
        {
            Lang l = Add(name, exts);
            l.KwText = kw; l.TypeText = types; l.ConstText = consts;
            l.LineCmt = A("//");
            Block(l, "/*", "*/");
            l.Quotes = "\"'";
            return l;
        }

        static void Define()
        {
            Plain = Add("Plain Text", ".txt .text");
            Plain.Func = false;

            const string cKw = "auto break case char const continue default do double else enum extern float for goto if inline int long register restrict return short signed sizeof static struct switch typedef union unsigned void volatile while _Bool _Complex _Atomic _Thread_local _Static_assert _Noreturn _Alignas _Alignof _Generic bool";
            const string cTypes = "size_t ssize_t ptrdiff_t intptr_t uintptr_t int8_t int16_t int32_t int64_t uint8_t uint16_t uint32_t uint64_t wchar_t FILE BOOL BYTE WORD DWORD QWORD HANDLE HWND HINSTANCE HMODULE LPSTR LPCSTR LPWSTR LPCWSTR LPVOID UINT ULONG LONG va_list";

            Lang c = CLike("C", ".c .h", cKw, cTypes, "NULL true false EOF stdin stdout stderr TRUE FALSE");
            c.HashPre = true;

            Lang cpp = CLike("C++", ".cpp .cc .cxx .c++ .hpp .hh .hxx .h++ .ino .ipp .tpp .inl .cu .cuh",
                cKw + " alignas alignof and and_eq asm bitand bitor catch class compl concept consteval constexpr constinit const_cast co_await co_return co_yield decltype delete dynamic_cast explicit export final friend mutable namespace new noexcept not not_eq operator or or_eq override private protected public reinterpret_cast requires static_assert static_cast template thread_local throw try typeid typename using virtual xor xor_eq char8_t char16_t char32_t",
                cTypes + " std string wstring string_view vector map set unordered_map unordered_set list deque array pair tuple optional variant shared_ptr unique_ptr weak_ptr function thread mutex",
                "NULL true false nullptr this");
            cpp.HashPre = true;

            Lang objc = CLike("Objective-C", ".m .mm",
                cKw + " id self super in out inout bycopy byref oneway",
                cTypes + " NSString NSArray NSDictionary NSObject NSInteger NSUInteger CGFloat SEL Class IMP",
                "nil Nil YES NO NULL true false self super");
            objc.HashPre = true; objc.AtPre = true;

            Lang cs = CLike("C#", ".cs .csx .cake",
                "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch throw try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias ascending async await by descending dynamic equals from get global group into join let nameof nint not nuint on or orderby partial record remove required select set unmanaged value var when where with yield init and file scoped",
                "String Object Int32 Int64 Int16 UInt32 Boolean Double Single Decimal Char Byte DateTime TimeSpan Guid List Dictionary HashSet Queue Stack IEnumerable IList IDictionary Task Action Func Exception Console Math StringBuilder Span",
                "true false null this");
            cs.HashPre = true;
            Ml(cs, "@\"", "\"", false, true);
            Ml(cs, "\"\"\"", "\"\"\"", false, false);

            const string javaKw = "abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float for goto if implements import instanceof int interface long native new package private protected public return short static strictfp super switch synchronized throw throws transient try void volatile while var record sealed permits yield";
            const string javaTypes = "String Object Integer Long Short Byte Double Float Boolean Character Number Math System Thread Runnable Exception RuntimeException List ArrayList LinkedList Map HashMap TreeMap Set HashSet Optional Stream StringBuilder";
            Lang java = CLike("Java", ".java .jav", javaKw, javaTypes, "true false null this super");
            java.AtPre = true;
            Ml(java, "\"\"\"", "\"\"\"", true, false);

            Lang groovy = CLike("Groovy", ".groovy .gradle .gvy jenkinsfile", javaKw + " def as in trait", javaTypes, "true false null this super it");
            groovy.AtPre = true;
            Ml(groovy, "\"\"\"", "\"\"\"", true, false);
            Ml(groovy, "'''", "'''", true, false);

            Lang kotlin = CLike("Kotlin", ".kt .kts",
                "abstract actual annotation as break by catch class companion const constructor continue crossinline data delegate do dynamic else enum expect external field file final finally for fun get if import in infix init inline inner interface internal is lateinit noinline object open operator out override package param private property protected public receiver reified return sealed set setparam super suspend tailrec throw try typealias typeof val value var vararg when where while",
                "Any Boolean Byte Char Double Float Int Long Nothing Short String Unit Array List MutableList Map MutableMap Set MutableSet Sequence Pair Triple",
                "true false null this super it");
            kotlin.AtPre = true;
            Ml(kotlin, "\"\"\"", "\"\"\"", false, false);

            Lang scala = CLike("Scala", ".scala .sc",
                "abstract case catch class def do else enum export extends extension final finally for forSome given if implicit import lazy match new object override package private protected return sealed then throw trait try type using val var while with yield",
                "Int Long Short Byte Double Float Boolean Char String Unit Any AnyRef AnyVal Nothing Null Option Some None List Seq Vector Map Set Array Either Left Right Future",
                "true false null this super");
            scala.AtPre = true;
            Ml(scala, "\"\"\"", "\"\"\"", false, false);

            Lang swift = CLike("Swift", ".swift",
                "actor associatedtype async await break case catch class continue convenience default defer deinit didSet do dynamic else enum extension fallthrough fileprivate final for func get guard if import in indirect infix init inout internal is lazy let mutating nonisolated nonmutating open operator optional override postfix precedencegroup prefix private protocol public repeat required rethrows return set some static struct subscript super switch throw throws try typealias unowned var weak where while willSet any",
                "Int Int8 Int16 Int32 Int64 UInt UInt8 UInt16 UInt32 UInt64 Float Double Bool String Character Array Dictionary Set Optional Result Void Any AnyObject Error Never",
                "true false nil self Self super");
            swift.AtPre = true; swift.HashPre = true; swift.Quotes = "\"";
            Ml(swift, "\"\"\"", "\"\"\"", true, false);

            Lang dart = CLike("Dart", ".dart",
                "abstract as assert async await base break case catch class const continue covariant default deferred do dynamic else enum export extends extension external factory final finally for Function get hide if implements import in interface is late library mixin new on operator part required rethrow return sealed set show static super switch sync throw try typedef var void when while with yield",
                "int double num String bool List Map Set Iterable Future Stream Object Null Never Record Duration DateTime",
                "true false null this super");
            dart.AtPre = true;
            Ml(dart, "\"\"\"", "\"\"\"", true, false);
            Ml(dart, "'''", "'''", true, false);

            const string jsKw = "async await break case catch class const continue debugger default delete do else export extends finally for from function get if import in instanceof let new of return set static super switch throw try typeof var void while with yield";
            const string jsTypes = "Array Boolean Date Error JSON Map Math Number Object Promise Proxy Reflect RegExp Set String Symbol WeakMap WeakSet BigInt console window document globalThis";
            const string jsConsts = "true false null undefined NaN Infinity this";
            JS = CLike("JavaScript", ".js .mjs .cjs .jsx .es6", jsKw, jsTypes, jsConsts);
            JS.IdentExtra = "$";
            Ml(JS, "`", "`", true, false);

            Lang ts = CLike("TypeScript", ".ts .tsx .mts .cts",
                jsKw + " abstract any as asserts bigint boolean declare enum implements infer interface is keyof module namespace never number object override private protected public readonly require satisfies string symbol type unique unknown accessor",
                jsTypes + " Record Partial Required Readonly Pick Omit Exclude Extract NonNullable ReturnType Parameters",
                jsConsts);
            ts.IdentExtra = "$"; ts.AtPre = true;
            Ml(ts, "`", "`", true, false);

            Lang json = Add("JSON", ".json .jsonc .json5 .webmanifest .har .geojson .babelrc .eslintrc .prettierrc .jshintrc");
            json.ConstText = "true false null"; json.Quotes = "\""; json.LineCmt = A("//");
            Block(json, "/*", "*/");
            json.ColonKeys = 1; json.Func = false;

            Lang go = CLike("Go", ".go",
                "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var",
                "bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any comparable",
                "true false nil iota");
            Ml(go, "`", "`", false, false);

            Lang rust = CLike("Rust", ".rs",
                "as async await break const continue crate dyn else enum extern fn for if impl in let loop match mod move mut pub ref return static struct super trait type unsafe use where while macro_rules union",
                "i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box Rc Arc RefCell HashMap HashSet BTreeMap Self",
                "true false None Some Ok Err self");
            rust.ShortSingle = true; rust.BangMacros = true; rust.HashAttr = true;

            Lang shader = CLike("Shader (GLSL/HLSL)", ".glsl .vert .frag .geom .comp .tesc .tese .hlsl .fx .fxh .shader .cginc .compute .wgsl",
                cKw + " uniform attribute varying in out inout layout precision highp mediump lowp discard cbuffer register packoffset groupshared numthreads",
                "vec2 vec3 vec4 ivec2 ivec3 ivec4 uvec2 uvec3 uvec4 bvec2 bvec3 bvec4 mat2 mat3 mat4 sampler2D sampler3D samplerCube texture2D float2 float3 float4 float2x2 float3x3 float4x4 half half2 half3 half4 int2 int3 int4 uint2 uint3 uint4 Texture2D SamplerState RWTexture2D StructuredBuffer RWStructuredBuffer",
                "true false");
            shader.HashPre = true;

            Lang php = Add("PHP", ".php .phtml .php3 .php4 .php5 .phps");
            php.Mode = 1; php.EmbedPhp = true; php.EmbedScripts = true; php.IgnoreCase = true;
            PhpCode = new Lang();
            PhpCode.Name = "PHP code";
            PhpCode.IgnoreCase = true;
            PhpCode.KwText = "abstract and array as break callable case catch class clone const continue declare default do echo else elseif empty enddeclare endfor endforeach endif endswitch endwhile enum extends final finally fn for foreach function global goto if implements include include_once instanceof insteadof interface isset list match namespace new or print private protected public readonly require require_once return static switch throw trait try unset use var while xor yield";
            PhpCode.TypeText = "int float bool string void mixed object iterable never self parent";
            PhpCode.ConstText = "true false null __CLASS__ __DIR__ __FILE__ __FUNCTION__ __LINE__ __METHOD__ __NAMESPACE__";
            PhpCode.LineCmt = A("//", "#");
            Block(PhpCode, "/*", "*/");
            PhpCode.Quotes = "\"'`"; PhpCode.VarPrefix = "$";

            Lang html = Add("HTML", ".html .htm .xhtml .shtml .vue .svelte .cshtml .razor .asp .aspx .jsp .hbs .ejs .mustache .twig");
            html.Mode = 1; html.EmbedScripts = true; html.IgnoreCase = true;

            Lang xml = Add("XML", ".xml .xaml .axaml .svg .xsd .xsl .xslt .wsdl .csproj .vbproj .vcxproj .fsproj .proj .props .targets .nuspec .resx .config .manifest .plist .rss .atom .kml .gpx .xliff .ui .qrc .storyboard .xib .wxs .pom .mxml .xul");
            xml.Mode = 1;

            const string cssKw = "important auto none inherit initial unset revert block inline inline-block flex inline-flex grid contents absolute relative fixed static sticky solid dashed dotted double hidden visible scroll bold bolder lighter normal italic center left right top bottom middle baseline transparent currentColor pointer default nowrap wrap uppercase lowercase capitalize ease ease-in ease-out ease-in-out linear infinite alternate forwards backwards both row column space-between space-around space-evenly stretch start end cover contain repeat no-repeat";
            CSS = Add("CSS", ".css");
            CSS.Quotes = "\"'"; Block(CSS, "/*", "*/");
            CSS.IdentExtra = "-"; CSS.ColonKeys = 1; CSS.AtPre = true; CSS.HashColors = true; CSS.PercentUnits = true;
            CSS.KwText = cssKw; CSS.IgnoreCase = true;

            Lang scss = Add("SCSS / Less", ".scss .sass .less .styl");
            scss.Quotes = "\"'"; Block(scss, "/*", "*/"); scss.LineCmt = A("//");
            scss.IdentExtra = "-"; scss.ColonKeys = 1; scss.AtPre = true; scss.HashColors = true; scss.PercentUnits = true;
            scss.KwText = cssKw; scss.IgnoreCase = true; scss.VarPrefix = "$";

            Lang py = Add("Python", ".py .pyw .pyi .pyx .pxd .gyp .bzl .star sconstruct sconscript");
            py.KwText = "and as assert async await break class continue def del elif else except finally for from global if import in is lambda match case nonlocal not or pass raise return try while with yield";
            py.TypeText = "int float complex str bytes bytearray bool list tuple dict set frozenset object type range Exception BaseException ValueError TypeError KeyError IndexError AttributeError RuntimeError StopIteration OSError IOError";
            py.ConstText = "True False None self cls __name__ __file__ __init__";
            py.LineCmt = A("#"); py.Quotes = "\"'"; py.AtPre = true;
            Ml(py, "\"\"\"", "\"\"\"", true, false);
            Ml(py, "'''", "'''", true, false);

            Lang rb = Add("Ruby", ".rb .rake .gemspec .ru .erb .rbw gemfile rakefile podfile vagrantfile brewfile guardfile");
            rb.KwText = "alias and begin break case class def defined do else elsif end ensure for if in module next not or redo rescue retry return super then undef unless until when while yield require require_relative include extend attr_accessor attr_reader attr_writer private protected public raise lambda proc puts";
            rb.ConstText = "true false nil self __FILE__ __LINE__";
            rb.LineCmt = A("#"); rb.Quotes = "\"'`"; rb.VarPrefix = "@$"; rb.ColonSymbols = true;
            Block(rb, "=begin", "=end");

            Lang lua = Add("Lua", ".lua .luau .rockspec .wlua");
            lua.KwText = "and break do else elseif end for function goto if in local not or repeat return then until while";
            lua.TypeText = "string table math io os coroutine utf8 debug package";
            lua.ConstText = "true false nil self _G _VERSION";
            Block(lua, "--[[", "]]");
            lua.LineCmt = A("--"); lua.Quotes = "\"'";
            Ml(lua, "[[", "]]", false, false);

            Lang sh = Add("Shell", ".sh .bash .zsh .ksh .fish .command .bashrc .zshrc .profile .bash_profile .bash_aliases .zprofile .envrc .gitignore .dockerignore .gitattributes .npmignore .hgignore pkgbuild apkbuild");
            sh.KwText = "if then else elif fi case esac for select while until do done in function time return exit break continue local export readonly declare typeset unset shift source alias eval exec set trap echo printf read cd test let";
            sh.ConstText = "true false";
            sh.LineCmt = A("#"); sh.Quotes = "\"'`"; sh.VarPrefix = "$"; sh.VarSpecial = true;

            Lang ps = Add("PowerShell", ".ps1 .psm1 .psd1 .ps1xml .pssc");
            ps.IgnoreCase = true;
            ps.KwText = "begin break catch class continue data define do dynamicparam else elseif end enum exit filter finally for foreach from function hidden if in param process return static switch throw trap try until using var while workflow -eq -ne -gt -ge -lt -le -like -notlike -match -notmatch -contains -notcontains -in -notin -replace -creplace -ireplace -and -or -not -xor -band -bor -bxor -bnot -is -isnot -as -split -join -f -ceq -cne -clike -cmatch -imatch -shl -shr";
            ps.TypeText = "string int long double bool byte char array hashtable object psobject pscustomobject datetime void xml regex scriptblock";
            ps.ConstText = "$true $false $null $_ $PSItem $args $this $input";
            ps.LineCmt = A("#"); Block(ps, "<#", "#>");
            ps.Quotes = "\"'"; ps.Esc = '`'; ps.VarPrefix = "$"; ps.VarColon = true; ps.DashWords = true; ps.IdentExtra = "-";
            Ml(ps, "@\"", "\"@", false, false);
            Ml(ps, "@'", "'@", false, false);

            Lang bat = Add("Batch", ".bat .cmd .btm");
            bat.Mode = 4; bat.IgnoreCase = true;
            bat.KwText = "echo set if else goto call exit setlocal endlocal enabledelayedexpansion disabledelayedexpansion enableextensions disableextensions for in do not exist defined errorlevel cmdextversion equ neq lss leq gtr geq cd chdir pushd popd shift start pause cls title color copy move del erase md mkdir rd rmdir ren rename type find findstr choice timeout where tasklist taskkill reg sc net xcopy robocopy dir mklink attrib nul con prn aux off on ver vol path prompt assoc ftype break verify date time wmic powershell cmd";

            Lang sql = Add("SQL", ".sql .ddl .dml .pgsql .psql .mysql .hql .prc .udf .tsql");
            sql.IgnoreCase = true;
            sql.KwText = "add all alter and any as asc authorization backup begin between break browse bulk by cascade case check checkpoint close clustered coalesce collate column commit compute constraint contains continue convert create cross current current_date current_time current_timestamp current_user cursor database dbcc deallocate declare default delete deny desc disk distinct distributed drop dump else end errlvl escape except exec execute exists exit external fetch file fillfactor for foreign freetext from full function go goto grant group having holdlock identity if in index inner insert intersect into is join key kill left like limit lineno load merge national nocheck nonclustered not null nullif of off offsets on open option or order outer over percent pivot plan precision primary print proc procedure public raiserror read readtext reconfigure references replace replication restore restrict return returning returns revert revoke right rollback rowcount rowguidcol rule save schema select session_user set setuser shutdown some statistics system_user table tablesample textsize then to top tran transaction trigger truncate try_convert tsequal union unique unpivot update updatetext use user using values varying view waitfor when where while with within writetext offset next rows only partition window lateral recursive temporary temp language plpgsql do loop raise notice explain analyze vacuum autoincrement auto_increment engine charset unsigned zerofill sequence materialized";
            sql.TypeText = "int integer bigint smallint tinyint bit decimal numeric money smallmoney float real date datetime datetime2 datetimeoffset smalldatetime time timestamp timestamptz interval char varchar nchar nvarchar text ntext binary varbinary image uniqueidentifier xml json jsonb boolean bool serial bigserial blob clob uuid bytea double sql_variant";
            sql.ConstText = "null true false";
            sql.LineCmt = A("--"); Block(sql, "/*", "*/");
            sql.Quotes = "'\""; sql.Esc = '\0'; sql.VarPrefix = "@";

            Lang perl = Add("Perl", ".pl .pm .t .pod .psgi .cgi");
            perl.KwText = "my our local sub if elsif else unless while until for foreach do last next redo return use no require package BEGIN END and or not eq ne lt gt le ge cmp print printf say die warn eval defined undef wantarray chomp chop push pop shift unshift splice split join map grep sort reverse keys values each exists delete open close binmode ref bless qw";
            perl.ConstText = "__FILE__ __LINE__ __PACKAGE__ __END__ __DATA__ STDIN STDOUT STDERR";
            perl.LineCmt = A("#"); perl.Quotes = "\"'`"; perl.VarPrefix = "$@%"; perl.VarSpecial = true;

            Lang r = Add("R", ".r .rprofile .rhistory");
            r.KwText = "if else repeat while function for in next break return switch library require source tryCatch stop warning";
            r.ConstText = "TRUE FALSE NULL NA NaN Inf T F NA_integer_ NA_real_ NA_character_";
            r.LineCmt = A("#"); r.Quotes = "\"'`"; r.IdentExtra = ".";

            Lang vb = Add("Visual Basic", ".vb .vbs .bas .cls .frm .vba .vbe .wsf");
            vb.IgnoreCase = true;
            vb.KwText = "addhandler addressof alias and andalso as boolean byref byte byval call case catch cbool cbyte cchar cdate cdbl cdec char cint class clng cobj const continue csbyte cshort csng cstr ctype cuint culng cushort date decimal declare default delegate dim directcast do double each else elseif end endif enum erase error event exit finally for friend function get gettype global gosub goto handles if implements imports in inherits integer interface is isnot let lib like long loop mod module mustinherit mustoverride mybase myclass namespace narrowing new next not notinheritable notoverridable object of on operator option optional or orelse overloads overridable overrides paramarray partial private property protected public raiseevent readonly redim removehandler resume return sbyte select set shadows shared short single static step stop string structure sub synclock then throw to try trycast typeof uinteger ulong ushort using variant wend when while widening with withevents writeonly xor";
            vb.ConstText = "true false nothing null empty me";
            vb.LineCmt = A("'", "rem"); vb.Quotes = "\""; vb.Esc = '\0'; vb.HashPre = true;

            Lang pas = Add("Pascal", ".pas .pp .dpr .dpk .lpr .lfm");
            pas.IgnoreCase = true;
            pas.KwText = "and array as asm begin case class const constructor destructor div do downto else end except exports file finalization finally for function goto if implementation in inherited initialization inline interface is label library mod not object of on or out packed procedure program property raise record repeat resourcestring set shl shr string then threadvar to try type unit until uses var while with xor override virtual abstract private protected public published strict";
            pas.TypeText = "integer cardinal shortint smallint longint int64 byte word longword boolean char real single double extended currency pointer variant tobject tstringlist";
            pas.ConstText = "true false nil self result";
            pas.LineCmt = A("//"); Block(pas, "{", "}"); Block(pas, "(*", "*)");
            pas.Quotes = "'"; pas.Esc = '\0';

            Lang asm = Add("Assembly", ".asm .s .nasm .masm");
            asm.IgnoreCase = true;
            asm.KwText = "mov movzx movsx lea add sub mul imul div idiv inc dec neg and or xor not shl shr sal sar rol ror cmp test jmp je jne jz jnz jg jge jl jle ja jae jb jbe jc jnc jo jno js jns call ret push pop nop int syscall sysenter leave enter loop rep repe repne movs stos lods cmps scas cld std cli sti hlt cpuid xchg cmov cmove cmovne bt bts btr setz setnz sete setne db dw dd dq resb resw resd resq times equ section segment global extern bits org align .section .text .data .bss .global .globl .extern .byte .word .long .quad .ascii .asciz .string .align .equ .set .macro .endm .include";
            asm.TypeText = "rax rbx rcx rdx rsi rdi rsp rbp r8 r9 r10 r11 r12 r13 r14 r15 eax ebx ecx edx esi edi esp ebp ax bx cx dx si di sp bp al ah bl bh cl ch dl dh cs ds es fs gs ss rip eip xmm0 xmm1 xmm2 xmm3 xmm4 xmm5 xmm6 xmm7 byte word dword qword ptr";
            asm.LineCmt = A(";"); asm.Quotes = "\"'"; asm.ColonKeys = 1; asm.IdentExtra = "."; asm.Func = false;

            Lang yaml = Add("YAML", ".yml .yaml .clang-format .clang-tidy");
            yaml.ConstText = "true false null yes no on off True False Null Yes No On Off TRUE FALSE NULL YES NO ON OFF";
            yaml.LineCmt = A("#"); yaml.Quotes = "\"'"; yaml.ColonKeys = 2; yaml.IdentExtra = "-."; yaml.Func = false;

            Lang toml = Add("TOML", ".toml .lock");
            toml.Mode = 2;

            Lang ini = Add("INI / Config", ".ini .cfg .conf .cnf .properties .prefs .inf .reg .desktop .editorconfig .gitconfig .gitmodules .npmrc .env .service .timer .socket .mount .url .hgrc .pylintrc .flake8 .coveragerc");
            ini.Mode = 2;

            Lang md = Add("Markdown", ".md .markdown .mdown .mkd .mkdn .mdx .rmd");
            md.Mode = 3;

            Lang diff = Add("Diff / Patch", ".diff .patch .rej");
            diff.Mode = 5;

            Lang mk = Add("Makefile", ".mk .mak .make makefile gnumakefile");
            mk.KwText = "ifeq ifneq ifdef ifndef else endif include sinclude define endef export unexport override private vpath";
            mk.LineCmt = A("#"); mk.Quotes = "\"'"; mk.VarPrefix = "$"; mk.VarParen = true; mk.VarSpecial = true;
            mk.ColonKeys = 2; mk.IdentExtra = ".-"; mk.Func = false;

            Lang docker = Add("Dockerfile", ".dockerfile dockerfile containerfile");
            docker.KwText = "FROM RUN CMD LABEL MAINTAINER EXPOSE ENV ADD COPY ENTRYPOINT VOLUME USER WORKDIR ARG ONBUILD STOPSIGNAL HEALTHCHECK SHELL AS";
            docker.LineCmt = A("#"); docker.Quotes = "\"'"; docker.VarPrefix = "$"; docker.Func = false;

            Lang cmake = Add("CMake", ".cmake cmakelists.txt");
            cmake.IgnoreCase = true;
            cmake.KwText = "if elseif else endif foreach endforeach while endwhile function endfunction macro endmacro return break continue set unset option project cmake_minimum_required cmake_policy add_executable add_library add_subdirectory add_custom_command add_custom_target add_dependencies add_test target_link_libraries target_include_directories target_compile_definitions target_compile_options target_compile_features target_sources include_directories link_directories find_package find_library find_path find_program message install include list string file math get_filename_component configure_file add_definitions add_compile_options set_target_properties set_property get_property enable_testing execute_process";
            cmake.ConstText = "ON OFF TRUE FALSE YES NO IGNORE NOTFOUND";
            cmake.LineCmt = A("#"); cmake.Quotes = "\""; cmake.VarPrefix = "$";

            Lang log = Add("Log", ".log .out");
            log.KwText = "ERROR FATAL CRITICAL CRIT SEVERE FAIL FAILED FAILURE EXCEPTION Error Fatal Exception Failed error fatal failed exception";
            log.TypeText = "WARN WARNING Warning warning WRN";
            log.ConstText = "INFO DEBUG TRACE VERBOSE NOTICE Info Debug Trace INF DBG TRC";
            log.Quotes = "\""; log.Esc = '\0'; log.Func = false;
        }

        public static int Tokenize(Lang L, string s, int state, byte[] k)
        {
            if (L == null || L == Plain || s.Length > MaxLineLen) return state;
            switch (L.Mode)
            {
                case 1: return Markup(L, s, state, k);
                case 2: Ini(L, s, k); return 0;
                case 3: return Markdown(s, state, k);
                case 4: Batch(L, s, k); return 0;
                case 5: Diff(s, k); return 0;
                default: return Code(L, s, 0, s.Length, state, k);
            }
        }

        static void Mark(byte[] k, int a, int b, byte kind)
        {
            if (k == null) return;
            for (int i = a; i < b; i++) k[i] = kind;
        }

        static bool IsDigit(char c) { return c >= '0' && c <= '9'; }
        static bool IsHex(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }
        public static bool IsIdStart(char c) { return char.IsLetter(c) || c == '_'; }
        public static bool IsIdChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        static bool At(string s, int i, int n, string t, bool ic)
        {
            if (i + t.Length > n) return false;
            if (!ic) return string.CompareOrdinal(s, i, t, 0, t.Length) == 0;
            return string.Compare(s, i, t, 0, t.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        static int Find(string s, string t, int from, int n, bool ic)
        {
            int last = n - t.Length;
            char c0 = t[0];
            for (int i = from; i <= last; i++)
            {
                char c = s[i];
                if (ic ? char.ToLowerInvariant(c) != char.ToLowerInvariant(c0) : c != c0) continue;
                if (At(s, i, n, t, ic)) return i;
            }
            return -1;
        }

        static int MatchAny(string s, int i, int n, string[] toks, bool ic)
        {
            for (int j = 0; j < toks.Length; j++) if (At(s, i, n, toks[j], ic)) return j;
            return -1;
        }

        static int FindClose(string s, int i, int n, string close, char esc, bool dbl)
        {
            char c0 = close[0];
            int cl = close.Length;
            while (i < n)
            {
                char c = s[i];
                if (esc != '\0' && c == esc) { i += 2; continue; }
                if (c == c0 && i + cl <= n && string.CompareOrdinal(s, i, close, 0, cl) == 0)
                {
                    if (dbl && cl == 1 && i + 1 < n && s[i + 1] == c0) { i += 2; continue; }
                    return i + cl;
                }
                i++;
            }
            return -1;
        }

        static string QuoteStr(char c)
        {
            switch (c) { case '"': return "\""; case '\'': return "'"; case '`': return "`"; default: return c.ToString(); }
        }

        static char NextNonSpace(string s, int i, int n)
        {
            while (i < n && (s[i] == ' ' || s[i] == '\t')) i++;
            return i < n ? s[i] : '\0';
        }

        static bool IsLineComment(Lang L, string s, int i, int n)
        {
            string[] lc = L.LineCmt;
            for (int j = 0; j < lc.Length; j++)
            {
                string t = lc[j];
                if (!At(s, i, n, t, L.IgnoreCase)) continue;
                if (IsIdStart(t[0]))
                {
                    if (i > 0 && IsIdChar(s[i - 1])) continue;
                    int e = i + t.Length;
                    if (e < n && IsIdChar(s[e])) continue;
                }
                return true;
            }
            return false;
        }

        static int ShortQuote(string s, int i, int n)
        {
            if (i + 2 < n && s[i + 1] != '\\' && s[i + 2] == '\'') return i + 3;
            if (i + 1 < n && s[i + 1] == '\\')
            {
                int e = s.IndexOf('\'', i + 2);
                if (e > 0 && e < n && e - i <= 12) return e + 1;
            }
            if (i + 3 < n && char.IsHighSurrogate(s[i + 1]) && s[i + 3] == '\'') return i + 4;
            return -1;
        }

        static int ScanNumber(Lang L, string s, int i, int n)
        {
            int e = i;
            if (s[e] == '0' && e + 1 < n && (s[e + 1] == 'x' || s[e + 1] == 'X'))
            {
                e += 2;
                while (e < n && (IsHex(s[e]) || s[e] == '_')) e++;
            }
            else
            {
                while (e < n && (IsDigit(s[e]) || s[e] == '_' || s[e] == '.'))
                {
                    if (s[e] == '.' && e + 1 < n && s[e + 1] == '.') break;
                    e++;
                }
                if (e < n && (s[e] == 'e' || s[e] == 'E'))
                {
                    int f = e + 1;
                    if (f < n && (s[f] == '+' || s[f] == '-')) f++;
                    if (f < n && IsDigit(s[f])) { e = f; while (e < n && IsDigit(s[e])) e++; }
                }
            }
            while (e < n && char.IsLetter(s[e])) e++;
            if (L.PercentUnits && e < n && s[e] == '%') e++;
            return e;
        }

        static int ScanVar(Lang L, string s, int i, int n)
        {
            int e = i + 1;
            if (e >= n) return i;
            char c = s[e];
            if (c == '{')
            {
                int close = s.IndexOf('}', e);
                return (close < 0 || close >= n) ? n : close + 1;
            }
            if (c == '(' && L.VarParen)
            {
                int close = s.IndexOf(')', e);
                return (close < 0 || close >= n) ? n : close + 1;
            }
            if (IsIdStart(c))
            {
                while (e < n && (IsIdChar(s[e]) || (L.VarColon && s[e] == ':' && e + 1 < n && IsIdStart(s[e + 1])))) e++;
                return e;
            }
            if (L.VarSpecial && (IsDigit(c) || "@#?$!*-".IndexOf(c) >= 0)) return e + 1;
            return i;
        }

        static void Classify(Lang L, string s, int i, int e, int n, byte[] k)
        {
            string w = s.Substring(i, e - i);
            byte kind = Tk.Text;
            if (L.Consts.Contains(w)) kind = Tk.Constant;
            else if (L.Kw.Contains(w)) kind = Tk.Keyword;
            else if (L.Types.Contains(w)) kind = Tk.Type;
            else
            {
                int j = e;
                while (j < n && s[j] == ' ') j++;
                if (j < n)
                {
                    char d = s[j];
                    if (d == '(' && L.Func) kind = Tk.Function;
                    else if (d == '!' && L.BangMacros && (j + 1 >= n || s[j + 1] != '=')) kind = Tk.Function;
                    else if (d == ':' && L.ColonKeys > 0 && (j + 1 >= n || s[j + 1] != ':'))
                    {
                        if (L.ColonKeys == 1 || j + 1 >= n || s[j + 1] == ' ' || s[j + 1] == '\t') kind = Tk.Attr;
                    }
                }
                if (kind == Tk.Text && L.DashWords && w.IndexOf('-') > 0) kind = Tk.Function;
            }
            if (kind != Tk.Text) Mark(k, i, e, kind);
        }

        static int Code(Lang L, string s, int i, int n, int state, byte[] k)
        {
            if (state != 0)
            {
                if (state < 100)
                {
                    int bi = state - 1;
                    if (bi < 0 || bi >= L.BlockOpen.Length) bi = 0;
                    if (L.BlockClose.Length == 0) return 0;
                    int e = Find(s, L.BlockClose[bi], i, n, L.IgnoreCase);
                    if (e < 0) { Mark(k, i, n, Tk.Comment); return state; }
                    e += L.BlockClose[bi].Length;
                    Mark(k, i, e, Tk.Comment);
                    i = e;
                }
                else
                {
                    int mi = state - 100;
                    if (mi < 0 || mi >= L.MlOpen.Length) return 0;
                    int e = FindClose(s, i, n, L.MlClose[mi], L.MlEsc[mi] ? L.Esc : '\0', L.MlDouble[mi]);
                    if (e < 0) { Mark(k, i, n, Tk.String); return state; }
                    Mark(k, i, e, Tk.String);
                    i = e;
                }
            }

            bool start = true;
            for (int p = 0; p < i; p++) if (s[p] != ' ' && s[p] != '\t') { start = false; break; }

            while (i < n)
            {
                char c = s[i];
                if (c == ' ' || c == '\t') { i++; continue; }
                bool atStart = start;
                start = false;

                if (atStart && L.HashPre && c == '#') { Mark(k, i, n, Tk.Preproc); return 0; }

                int m = MatchAny(s, i, n, L.BlockOpen, L.IgnoreCase);
                if (m >= 0)
                {
                    int e = Find(s, L.BlockClose[m], i + L.BlockOpen[m].Length, n, L.IgnoreCase);
                    if (e < 0) { Mark(k, i, n, Tk.Comment); return m + 1; }
                    e += L.BlockClose[m].Length;
                    Mark(k, i, e, Tk.Comment);
                    i = e;
                    continue;
                }

                if (IsLineComment(L, s, i, n)) { Mark(k, i, n, Tk.Comment); return 0; }

                m = MatchAny(s, i, n, L.MlOpen, false);
                if (m >= 0)
                {
                    int e = FindClose(s, i + L.MlOpen[m].Length, n, L.MlClose[m], L.MlEsc[m] ? L.Esc : '\0', L.MlDouble[m]);
                    if (e < 0) { Mark(k, i, n, Tk.String); return 100 + m; }
                    Mark(k, i, e, Tk.String);
                    i = e;
                    continue;
                }

                if (L.Quotes.IndexOf(c) >= 0)
                {
                    if (c == '\'' && L.ShortSingle)
                    {
                        int q = ShortQuote(s, i, n);
                        if (q < 0)
                        {
                            int e3 = i + 1;
                            while (e3 < n && IsIdChar(s[e3])) e3++;
                            Mark(k, i, e3, Tk.Type);
                            i = e3;
                            continue;
                        }
                        Mark(k, i, q, Tk.String);
                        i = q;
                        continue;
                    }
                    int e = FindClose(s, i + 1, n, QuoteStr(c), L.Esc, false);
                    if (e < 0) e = n;
                    byte kind = Tk.String;
                    if (L.ColonKeys > 0 && NextNonSpace(s, e, n) == ':') kind = Tk.Attr;
                    Mark(k, i, e, kind);
                    i = e;
                    continue;
                }

                if (L.DashWords && c == '-' && i + 1 < n && char.IsLetter(s[i + 1]) && (i == 0 || !IsIdChar(s[i - 1])))
                {
                    int e = i + 1;
                    while (e < n && IsIdChar(s[e])) e++;
                    if (k != null) Mark(k, i, e, L.Kw.Contains(s.Substring(i, e - i)) ? Tk.Keyword : Tk.Attr);
                    i = e;
                    continue;
                }

                if (L.VarPrefix.Length > 0 && L.VarPrefix.IndexOf(c) >= 0)
                {
                    int e = ScanVar(L, s, i, n);
                    if (e > i + 1)
                    {
                        byte kind = Tk.Variable;
                        if (k != null && L.Consts.Count > 0 && L.Consts.Contains(s.Substring(i, e - i))) kind = Tk.Constant;
                        Mark(k, i, e, kind);
                        i = e;
                        continue;
                    }
                }

                if (c == '@' && L.AtPre && i + 1 < n && IsIdStart(s[i + 1]))
                {
                    int e = i + 1;
                    while (e < n && (IsIdChar(s[e]) || s[e] == '.' || s[e] == '-')) e++;
                    Mark(k, i, e, Tk.Preproc);
                    i = e;
                    continue;
                }

                if (c == '#' && L.HashAttr && i + 1 < n && (s[i + 1] == '[' || (s[i + 1] == '!' && i + 2 < n && s[i + 2] == '[')))
                {
                    int e = s.IndexOf(']', i);
                    e = (e < 0 || e >= n) ? n : e + 1;
                    Mark(k, i, e, Tk.Preproc);
                    i = e;
                    continue;
                }

                if (c == '#' && L.HashColors && i + 1 < n && IsHex(s[i + 1]))
                {
                    int e = i + 1;
                    while (e < n && IsIdChar(s[e])) e++;
                    Mark(k, i, e, Tk.Number);
                    i = e;
                    continue;
                }

                if (c == ':' && L.ColonSymbols && i + 1 < n && IsIdStart(s[i + 1]) && (i == 0 || (s[i - 1] != ':' && !IsIdChar(s[i - 1]))))
                {
                    int e = i + 1;
                    while (e < n && IsIdChar(s[e])) e++;
                    Mark(k, i, e, Tk.Constant);
                    i = e;
                    continue;
                }

                if (IsDigit(c) || (c == '.' && i + 1 < n && IsDigit(s[i + 1])))
                {
                    int e = ScanNumber(L, s, i, n);
                    Mark(k, i, e, Tk.Number);
                    i = e;
                    continue;
                }

                bool extraStart = L.IdentExtra.Length > 0 && L.IdentExtra.IndexOf(c) >= 0 && i + 1 < n && IsIdStart(s[i + 1]);
                if (IsIdStart(c) || extraStart)
                {
                    int e = i + 1;
                    while (e < n)
                    {
                        char d = s[e];
                        if (IsIdChar(d)) { e++; continue; }
                        if (L.IdentExtra.Length > 0 && L.IdentExtra.IndexOf(d) >= 0 && e + 1 < n && IsIdChar(s[e + 1])) { e++; continue; }
                        break;
                    }
                    if (k != null) Classify(L, s, i, e, n, k);
                    i = e;
                    continue;
                }
                i++;
            }
            return 0;
        }

        static bool IsNameStart(char c) { return char.IsLetter(c) || c == '_' || c == ':'; }
        static bool IsNameChar(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '-' || c == '.'; }

        static int Markup(Lang L, string s, int state, byte[] k)
        {
            int n = s.Length, i = 0;
            while (i < n)
            {
                if (state >= 1000)
                {
                    int kind = state / 1000, sub = state % 1000;
                    if (kind == 3)
                    {
                        int pe = Find(s, "?>", i, n, false);
                        sub = Code(PhpCode, s, i, pe < 0 ? n : pe, sub, k);
                        if (pe < 0) return 3000 + sub;
                        Mark(k, pe, pe + 2, Tk.Preproc);
                        i = pe + 2;
                        state = 0;
                        continue;
                    }
                    int ce = Find(s, kind == 1 ? "</script" : "</style", i, n, true);
                    sub = Code(kind == 1 ? JS : CSS, s, i, ce < 0 ? n : ce, sub, k);
                    if (ce < 0) return kind * 1000 + sub;
                    i = ce;
                    state = 0;
                    continue;
                }
                int bs = state % 10, raw = state / 10;
                switch (bs)
                {
                    case 1:
                        {
                            int e = Find(s, "-->", i, n, false);
                            if (e < 0) { Mark(k, i, n, Tk.Comment); return 1; }
                            Mark(k, i, e + 3, Tk.Comment);
                            i = e + 3; state = 0;
                            continue;
                        }
                    case 5:
                        {
                            int e = Find(s, "]]>", i, n, false);
                            if (e < 0) { Mark(k, i, n, Tk.String); return 5; }
                            Mark(k, i, e + 3, Tk.String);
                            i = e + 3; state = 0;
                            continue;
                        }
                    case 3:
                    case 4:
                        {
                            int e = s.IndexOf(bs == 3 ? '"' : '\'', i);
                            if (e < 0) { Mark(k, i, n, Tk.String); return state; }
                            Mark(k, i, e + 1, Tk.String);
                            i = e + 1; state = 2 + raw * 10;
                            continue;
                        }
                    case 2:
                        {
                            char c = s[i];
                            if (c == '>')
                            {
                                Mark(k, i, i + 1, Tk.Tag);
                                i++;
                                state = raw == 1 ? 1000 : raw == 2 ? 2000 : 0;
                                continue;
                            }
                            if (c == '/' && i + 1 < n && s[i + 1] == '>') { Mark(k, i, i + 2, Tk.Tag); i += 2; state = 0; continue; }
                            if (c == '?' && i + 1 < n && s[i + 1] == '>') { Mark(k, i, i + 2, Tk.Preproc); i += 2; state = 0; continue; }
                            if (c == '"' || c == '\'')
                            {
                                Mark(k, i, i + 1, Tk.String);
                                i++;
                                state = (c == '"' ? 3 : 4) + raw * 10;
                                continue;
                            }
                            if (IsNameStart(c))
                            {
                                int e = i + 1;
                                while (e < n && IsNameChar(s[e])) e++;
                                Mark(k, i, e, Tk.Attr);
                                i = e;
                                continue;
                            }
                            i++;
                            continue;
                        }
                    default:
                        {
                            int j = i;
                            while (j < n && s[j] != '<' && s[j] != '&') j++;
                            if (j >= n) return 0;
                            i = j;
                            if (s[i] == '&')
                            {
                                int e = i + 1;
                                while (e < n && e - i < 12 && (char.IsLetterOrDigit(s[e]) || s[e] == '#')) e++;
                                if (e < n && s[e] == ';' && e > i + 1) { Mark(k, i, e + 1, Tk.Constant); i = e + 1; }
                                else i++;
                                continue;
                            }
                            if (L.EmbedPhp && At(s, i, n, "<?", false))
                            {
                                int st = i + 2;
                                if (At(s, st, n, "php", true)) st += 3;
                                else if (st < n && s[st] == '=') st++;
                                Mark(k, i, st, Tk.Preproc);
                                i = st; state = 3000;
                                continue;
                            }
                            if (At(s, i, n, "<!--", false)) { Mark(k, i, i + 4, Tk.Comment); i += 4; state = 1; continue; }
                            if (At(s, i, n, "<![CDATA[", false)) { Mark(k, i, i + 9, Tk.Preproc); i += 9; state = 5; continue; }
                            if (i + 1 < n && (s[i + 1] == '!' || s[i + 1] == '?'))
                            {
                                int e = i + 2;
                                while (e < n && IsNameChar(s[e])) e++;
                                Mark(k, i, e, Tk.Preproc);
                                i = e; state = 2;
                                continue;
                            }
                            if (i + 1 < n && (IsNameStart(s[i + 1]) || s[i + 1] == '/'))
                            {
                                int e = i + 1;
                                bool closing = s[e] == '/';
                                if (closing) e++;
                                int ns = e;
                                while (e < n && IsNameChar(s[e])) e++;
                                Mark(k, i, e, Tk.Tag);
                                int r = 0;
                                if (!closing && L.EmbedScripts)
                                {
                                    int len = e - ns;
                                    if (len == 6 && At(s, ns, n, "script", true)) r = 1;
                                    else if (len == 5 && At(s, ns, n, "style", true)) r = 2;
                                }
                                i = e; state = 2 + r * 10;
                                continue;
                            }
                            i++;
                            continue;
                        }
                }
            }
            return state;
        }

        static void Ini(Lang L, string s, byte[] k)
        {
            int n = s.Length, i = 0;
            while (i < n && (s[i] == ' ' || s[i] == '\t')) i++;
            if (i >= n) return;
            char c = s[i];
            if (c == ';' || c == '#' || c == '!') { Mark(k, i, n, Tk.Comment); return; }
            if (c == '[')
            {
                int e = s.IndexOf(']', i);
                Mark(k, i, e < 0 ? n : e + 1, Tk.Keyword);
                return;
            }
            int eq = -1;
            if (c == '"')
            {
                int q = FindClose(s, i + 1, n, "\"", '\\', false);
                if (q > 0) { eq = s.IndexOf('=', q); }
            }
            else
            {
                for (int j = i; j < n; j++) if (s[j] == '=' || s[j] == ':') { eq = j; break; }
            }
            if (eq < 0) return;
            Mark(k, i, eq, Tk.Attr);
            int v = eq + 1;
            while (v < n && (s[v] == ' ' || s[v] == '\t')) v++;
            if (v >= n) return;
            if (s[v] == '"' || s[v] == '\'')
            {
                int e = FindClose(s, v + 1, n, QuoteStr(s[v]), '\\', false);
                if (e < 0) e = n;
                Mark(k, v, e, Tk.String);
                v = e;
            }
            else
            {
                int e = v;
                while (e < n && s[e] != '#' && s[e] != ';') e++;
                int te = e;
                while (te > v && (s[te - 1] == ' ' || s[te - 1] == '\t')) te--;
                string val = s.Substring(v, te - v);
                double dummy;
                if (val == "true" || val == "false" || val == "True" || val == "False" || val == "yes" || val == "no")
                    Mark(k, v, te, Tk.Constant);
                else if (val.Length > 0 && double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dummy))
                    Mark(k, v, te, Tk.Number);
                v = e;
            }
            if (v < n)
            {
                int h = s.IndexOf('#', v);
                if (h >= 0 && (h == 0 || s[h - 1] == ' ' || s[h - 1] == '\t')) Mark(k, h, n, Tk.Comment);
            }
        }

        static int Markdown(string s, int state, byte[] k)
        {
            int n = s.Length, i = 0;
            while (i < n && (s[i] == ' ' || s[i] == '\t')) i++;
            bool fence = At(s, i, n, "```", false) || At(s, i, n, "~~~", false);
            if (state == 1)
            {
                if (fence) { Mark(k, 0, n, Tk.Preproc); return 0; }
                Mark(k, 0, n, Tk.String);
                return 1;
            }
            if (fence) { Mark(k, 0, n, Tk.Preproc); return 1; }
            if (i >= n) return 0;
            if (s[i] == '#') { Mark(k, 0, n, Tk.Heading); return 0; }
            if (s[i] == '>') { Mark(k, 0, n, Tk.Comment); return 0; }
            if (n - i >= 3)
            {
                char r = s[i];
                if (r == '-' || r == '*' || r == '_' || r == '=')
                {
                    bool rule = true;
                    for (int j = i; j < n; j++) if (s[j] != r && s[j] != ' ') { rule = false; break; }
                    if (rule) { Mark(k, 0, n, Tk.Comment); return 0; }
                }
            }
            if (i + 1 < n && (s[i] == '-' || s[i] == '*' || s[i] == '+') && s[i + 1] == ' ')
            {
                Mark(k, i, i + 1, Tk.Keyword);
                i += 2;
            }
            else
            {
                int j = i;
                while (j < n && IsDigit(s[j])) j++;
                if (j > i && j + 1 < n && (s[j] == '.' || s[j] == ')') && s[j + 1] == ' ') { Mark(k, i, j + 1, Tk.Keyword); i = j + 2; }
            }
            while (i < n)
            {
                char c = s[i];
                if (c == '`')
                {
                    int e = s.IndexOf('`', i + 1);
                    if (e > 0) { Mark(k, i, e + 1, Tk.String); i = e + 1; continue; }
                }
                else if ((c == '*' || c == '_') && i + 1 < n && s[i + 1] == c)
                {
                    int e = s.IndexOf(c == '*' ? "**" : "__", i + 2, StringComparison.Ordinal);
                    if (e > 0) { Mark(k, i, e + 2, Tk.Type); i = e + 2; continue; }
                }
                else if (c == '*' && i + 1 < n && s[i + 1] != ' ')
                {
                    int e = s.IndexOf('*', i + 1);
                    if (e > 0) { Mark(k, i, e + 1, Tk.Variable); i = e + 1; continue; }
                }
                else if (c == '[')
                {
                    int e = s.IndexOf(']', i + 1);
                    if (e > 0 && e + 1 < n && s[e + 1] == '(')
                    {
                        int p = s.IndexOf(')', e + 2);
                        if (p > 0)
                        {
                            Mark(k, i, e + 1, Tk.Function);
                            Mark(k, e + 1, p + 1, Tk.Attr);
                            i = p + 1;
                            continue;
                        }
                    }
                }
                i++;
            }
            return 0;
        }

        static void Batch(Lang L, string s, byte[] k)
        {
            int n = s.Length, i = 0;
            while (i < n && (s[i] == ' ' || s[i] == '\t')) i++;
            if (i < n && s[i] == '@') i++;
            if (At(s, i, n, "::", false)) { Mark(k, i, n, Tk.Comment); return; }
            if (At(s, i, n, "rem", true) && (i + 3 >= n || !IsIdChar(s[i + 3]))) { Mark(k, i, n, Tk.Comment); return; }
            if (i < n && s[i] == ':') { Mark(k, i, n, Tk.Function); return; }
            bool echo = false;
            while (i < n)
            {
                char c = s[i];
                if (c == '%')
                {
                    if (i + 1 < n && s[i + 1] == '%')
                    {
                        int e = i + 2;
                        if (e < n && s[e] == '~') { e++; while (e < n && char.IsLetter(s[e])) e++; }
                        else if (e < n && char.IsLetter(s[e])) e++;
                        Mark(k, i, e, Tk.Variable);
                        i = e;
                        continue;
                    }
                    if (i + 1 < n && (IsDigit(s[i + 1]) || s[i + 1] == '*')) { Mark(k, i, i + 2, Tk.Variable); i += 2; continue; }
                    if (i + 1 < n && s[i + 1] == '~')
                    {
                        int e = i + 2;
                        while (e < n && char.IsLetter(s[e])) e++;
                        if (e < n && (IsDigit(s[e]) || s[e] == '*')) e++;
                        Mark(k, i, e, Tk.Variable);
                        i = e;
                        continue;
                    }
                    int close = s.IndexOf('%', i + 1);
                    if (close > i + 1) { Mark(k, i, close + 1, Tk.Variable); i = close + 1; continue; }
                    i++;
                    continue;
                }
                if (c == '!')
                {
                    int close = s.IndexOf('!', i + 1);
                    if (close > i + 1 && s.IndexOf(' ', i + 1, close - i - 1) < 0) { Mark(k, i, close + 1, Tk.Variable); i = close + 1; continue; }
                    i++;
                    continue;
                }
                if (echo) { i++; continue; }
                if (c == '"')
                {
                    int e = s.IndexOf('"', i + 1);
                    e = e < 0 ? n : e + 1;

                    for (int j = i; j < e; j++) if (k != null && k[j] == Tk.Text) k[j] = Tk.String;
                    int j2 = i + 1;
                    while (j2 < e)
                    {
                        if (s[j2] == '%')
                        {
                            int cl = s.IndexOf('%', j2 + 1);
                            if (cl > j2 && cl < e) { Mark(k, j2, cl + 1, Tk.Variable); j2 = cl + 1; continue; }
                        }
                        j2++;
                    }
                    i = e;
                    continue;
                }
                if (IsIdStart(c))
                {
                    int e = i + 1;
                    while (e < n && (IsIdChar(s[e]) || s[e] == '-')) e++;
                    string w = s.Substring(i, e - i);
                    if (L.Kw.Contains(w))
                    {
                        Mark(k, i, e, Tk.Keyword);
                        if (string.Equals(w, "echo", StringComparison.OrdinalIgnoreCase)) echo = true;
                    }
                    i = e;
                    continue;
                }
                if (IsDigit(c) && (i == 0 || !IsIdChar(s[i - 1])))
                {
                    int e = i;
                    while (e < n && IsDigit(s[e])) e++;
                    Mark(k, i, e, Tk.Number);
                    i = e;
                    continue;
                }
                i++;
            }
        }

        static void Diff(string s, byte[] k)
        {
            int n = s.Length;
            if (n == 0) return;
            if (s.StartsWith("+++") || s.StartsWith("---") || s.StartsWith("diff ") || s.StartsWith("index ")) Mark(k, 0, n, Tk.Keyword);
            else if (s.StartsWith("@@")) Mark(k, 0, n, Tk.Preproc);
            else if (s[0] == '+' || s[0] == '>') Mark(k, 0, n, Tk.Comment);
            else if (s[0] == '-' || s[0] == '<') Mark(k, 0, n, Tk.String);
        }
    }
}
