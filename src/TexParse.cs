using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MinGent
{
    class TexSym
    {
        public string Text;
        public int Cls, Face, Limits;
        public bool Big;
    }

    static class Tex
    {
        public const int Ord = 0, Op = 1, Bin = 2, Rel = 3, Open = 4, Close = 5, Punct = 6, Inner = 7;
        public const int Italic = 0, Roman = 1, Bold = 2, BoldItalic = 3, Symbol = 4, Sans = 5, Mono = 6;

        public static readonly Dictionary<string, TexSym> Cmd = new Dictionary<string, TexSym>(StringComparer.Ordinal);
        public static readonly Dictionary<int, TexSym> Chars = new Dictionary<int, TexSym>();
        public static readonly Dictionary<int, int> Negated = new Dictionary<int, int>();

        static void S(int cls, int face, int cp, string names)
        {
            string t = char.ConvertFromUtf32(cp);
            foreach (string n in names.Split(' '))
            {
                if (n.Length == 0) continue;
                TexSym s = new TexSym();
                s.Text = t;
                s.Cls = cls;
                s.Face = face;
                Cmd[n] = s;
            }
            if (!Chars.ContainsKey(cp))
            {
                TexSym c = new TexSym();
                c.Text = t;
                c.Cls = cls;
                c.Face = face;
                Chars[cp] = c;
            }
        }

        static void Big(int cp, int limits, string names)
        {
            S(Op, Symbol, cp, names);
            foreach (string n in names.Split(' '))
            {
                if (n.Length == 0) continue;
                Cmd[n].Big = true;
                Cmd[n].Limits = limits;
            }
            Chars[cp].Big = true;
            Chars[cp].Limits = limits;
        }

        static void Fn(string names, bool limits)
        {
            foreach (string n in names.Split(' '))
            {
                if (n.Length == 0) continue;
                TexSym s = new TexSym();
                s.Text = n;
                s.Cls = Op;
                s.Face = Roman;
                s.Limits = limits ? 0 : -1;
                Cmd[n] = s;
            }
        }

        static void FnAs(string name, string text, bool limits)
        {
            TexSym s = new TexSym();
            s.Text = text;
            s.Cls = Op;
            s.Face = Roman;
            s.Limits = limits ? 0 : -1;
            Cmd[name] = s;
        }

        static void Neg(int a, int b) { Negated[a] = b; }

        static Tex()
        {
            S(Ord, Italic, 0x3B1, "alpha"); S(Ord, Italic, 0x3B2, "beta"); S(Ord, Italic, 0x3B3, "gamma"); S(Ord, Italic, 0x3B4, "delta");
            S(Ord, Italic, 0x3F5, "epsilon"); S(Ord, Italic, 0x3B5, "varepsilon"); S(Ord, Italic, 0x3B6, "zeta"); S(Ord, Italic, 0x3B7, "eta");
            S(Ord, Italic, 0x3B8, "theta"); S(Ord, Italic, 0x3D1, "vartheta"); S(Ord, Italic, 0x3B9, "iota"); S(Ord, Italic, 0x3BA, "kappa");
            S(Ord, Italic, 0x3F0, "varkappa"); S(Ord, Italic, 0x3BB, "lambda"); S(Ord, Italic, 0x3BC, "mu"); S(Ord, Italic, 0x3BD, "nu");
            S(Ord, Italic, 0x3BE, "xi"); S(Ord, Italic, 0x3BF, "omicron"); S(Ord, Italic, 0x3C0, "pi"); S(Ord, Italic, 0x3D6, "varpi");
            S(Ord, Italic, 0x3C1, "rho"); S(Ord, Italic, 0x3F1, "varrho"); S(Ord, Italic, 0x3C3, "sigma"); S(Ord, Italic, 0x3C2, "varsigma");
            S(Ord, Italic, 0x3C4, "tau"); S(Ord, Italic, 0x3C5, "upsilon"); S(Ord, Italic, 0x3D5, "phi"); S(Ord, Italic, 0x3C6, "varphi");
            S(Ord, Italic, 0x3C7, "chi"); S(Ord, Italic, 0x3C8, "psi"); S(Ord, Italic, 0x3C9, "omega"); S(Ord, Italic, 0x3DD, "digamma");
            S(Ord, Roman, 0x393, "Gamma"); S(Ord, Roman, 0x394, "Delta"); S(Ord, Roman, 0x398, "Theta"); S(Ord, Roman, 0x39B, "Lambda");
            S(Ord, Roman, 0x39E, "Xi"); S(Ord, Roman, 0x3A0, "Pi"); S(Ord, Roman, 0x3A3, "Sigma"); S(Ord, Roman, 0x3A5, "Upsilon");
            S(Ord, Roman, 0x3A6, "Phi"); S(Ord, Roman, 0x3A8, "Psi"); S(Ord, Roman, 0x3A9, "Omega");
            S(Ord, Italic, 0x393, "varGamma"); S(Ord, Italic, 0x394, "varDelta"); S(Ord, Italic, 0x398, "varTheta"); S(Ord, Italic, 0x39B, "varLambda");
            S(Ord, Italic, 0x39E, "varXi"); S(Ord, Italic, 0x3A0, "varPi"); S(Ord, Italic, 0x3A3, "varSigma"); S(Ord, Italic, 0x3A5, "varUpsilon");
            S(Ord, Italic, 0x3A6, "varPhi"); S(Ord, Italic, 0x3A8, "varPsi"); S(Ord, Italic, 0x3A9, "varOmega");
            S(Ord, Roman, 'A', "Alpha"); S(Ord, Roman, 'B', "Beta"); S(Ord, Roman, 'E', "Epsilon"); S(Ord, Roman, 'Z', "Zeta");
            S(Ord, Roman, 'H', "Eta"); S(Ord, Roman, 'I', "Iota"); S(Ord, Roman, 'K', "Kappa"); S(Ord, Roman, 'M', "Mu");
            S(Ord, Roman, 'N', "Nu"); S(Ord, Roman, 'O', "Omicron"); S(Ord, Roman, 'P', "Rho"); S(Ord, Roman, 'T', "Tau"); S(Ord, Roman, 'X', "Chi");
            S(Ord, Symbol, 0x2135, "aleph alef"); S(Ord, Symbol, 0x2136, "beth"); S(Ord, Symbol, 0x2137, "gimel"); S(Ord, Symbol, 0x2138, "daleth");

            S(Ord, Symbol, 0x221E, "infty infin infinity"); S(Ord, Symbol, 0x2202, "partial"); S(Ord, Symbol, 0x2207, "nabla");
            S(Ord, Symbol, 0x2200, "forall"); S(Ord, Symbol, 0x2203, "exists exist"); S(Ord, Symbol, 0x2204, "nexists");
            S(Ord, Symbol, 0xAC, "neg lnot"); S(Ord, Symbol, 0x2205, "emptyset varnothing empty");
            S(Ord, Symbol, 0x210F, "hbar hslash"); S(Ord, Symbol, 0x2113, "ell"); S(Ord, Symbol, 0x2118, "wp weierp");
            S(Ord, Symbol, 0x211C, "Re real"); S(Ord, Symbol, 0x2111, "Im image"); S(Ord, Symbol, 0x2032, "prime");
            S(Ord, Symbol, 0x2035, "backprime"); S(Ord, Symbol, 0x2220, "angle"); S(Ord, Symbol, 0x2221, "measuredangle");
            S(Ord, Symbol, 0x2222, "sphericalangle"); S(Ord, Symbol, 0x25B3, "triangle vartriangle bigtriangleup");
            S(Ord, Symbol, 0x25BD, "triangledown"); S(Ord, Symbol, 0x25A1, "square Box"); S(Ord, Symbol, 0x25A0, "blacksquare");
            S(Ord, Symbol, 0x25C7, "Diamond"); S(Ord, Symbol, 0x25CA, "lozenge"); S(Ord, Symbol, 0x29EB, "blacklozenge");
            S(Ord, Symbol, 0x2663, "clubsuit clubs"); S(Ord, Symbol, 0x2662, "diamondsuit diamonds"); S(Ord, Symbol, 0x2661, "heartsuit hearts");
            S(Ord, Symbol, 0x2660, "spadesuit spades"); S(Ord, Symbol, 0x266D, "flat"); S(Ord, Symbol, 0x266E, "natural"); S(Ord, Symbol, 0x266F, "sharp");
            S(Ord, Symbol, 0x22A4, "top"); S(Ord, Symbol, 0x22A5, "bot"); S(Ord, Symbol, 0x2020, "dag"); S(Ord, Symbol, 0x2021, "ddag");
            S(Ord, Roman, 0xA7, "S"); S(Ord, Roman, 0xB6, "P"); S(Ord, Roman, 0xA9, "copyright"); S(Ord, Symbol, 0x2713, "checkmark");
            S(Ord, Symbol, 0x2605, "bigstar"); S(Ord, Roman, 0xB0, "degree"); S(Ord, Symbol, 0x2127, "mho"); S(Ord, Symbol, 0x2201, "complement");
            S(Ord, Italic, 0xF0, "eth"); S(Ord, Italic, 0x131, "imath"); S(Ord, Italic, 0x237, "jmath"); S(Ord, Symbol, 0x2132, "Finv");
            S(Ord, Symbol, 0x2141, "Game"); S(Ord, Symbol, 0x221A, "surd"); S(Ord, Symbol, 0x2571, "diagup"); S(Ord, Symbol, 0x2572, "diagdown");
            S(Ord, Symbol, 0x2720, "maltese"); S(Ord, Roman, 0xAE, "circledR"); S(Ord, Roman, 0xA5, "yen"); S(Ord, Roman, 0xA3, "pounds");
            S(Ord, Roman, 0x20AC, "euro"); S(Ord, Symbol, '\\', "backslash"); S(Ord, Symbol, '|', "vert"); S(Ord, Symbol, 0x2016, "Vert");
            S(Ord, Symbol, 0x2102, "C Complex cnums"); S(Ord, Symbol, 0x2115, "N natnums"); S(Ord, Symbol, 0x211A, "Q");
            S(Ord, Symbol, 0x211D, "R Reals reals"); S(Ord, Symbol, 0x2124, "Z Integers");
            S(Inner, Symbol, 0x2026, "ldots dots dotso dotsc mathellipsis ellipsis"); S(Inner, Symbol, 0x22EF, "cdots dotsb dotsm dotsi");
            S(Ord, Symbol, 0x22EE, "vdots"); S(Inner, Symbol, 0x22F1, "ddots"); S(Inner, Symbol, 0x22F0, "iddots adots");

            S(Bin, Symbol, 0xB1, "pm"); S(Bin, Symbol, 0x2213, "mp"); S(Bin, Symbol, 0xD7, "times"); S(Bin, Symbol, 0xF7, "div");
            S(Bin, Symbol, 0x22C5, "cdot centerdot"); S(Bin, Symbol, 0x2217, "ast"); S(Bin, Symbol, 0x22C6, "star"); S(Bin, Symbol, 0x2218, "circ");
            S(Bin, Symbol, 0x2219, "bullet"); S(Bin, Symbol, 0x2295, "oplus"); S(Bin, Symbol, 0x2296, "ominus"); S(Bin, Symbol, 0x2297, "otimes");
            S(Bin, Symbol, 0x2298, "oslash"); S(Bin, Symbol, 0x2299, "odot"); S(Bin, Symbol, 0x229B, "circledast"); S(Bin, Symbol, 0x229A, "circledcirc");
            S(Bin, Symbol, 0x229D, "circleddash"); S(Bin, Symbol, 0x222A, "cup"); S(Bin, Symbol, 0x2229, "cap"); S(Bin, Symbol, 0x22D3, "Cup doublecup");
            S(Bin, Symbol, 0x22D2, "Cap doublecap"); S(Bin, Symbol, 0x2294, "sqcup"); S(Bin, Symbol, 0x2293, "sqcap"); S(Bin, Symbol, 0x2228, "vee lor");
            S(Bin, Symbol, 0x2227, "wedge land"); S(Bin, Symbol, 0x22CE, "curlyvee"); S(Bin, Symbol, 0x22CF, "curlywedge");
            S(Bin, Symbol, 0x2216, "setminus smallsetminus"); S(Bin, Symbol, 0x2240, "wr"); S(Bin, Symbol, 0x22C4, "diamond");
            S(Bin, Symbol, 0x25BD, "bigtriangledown"); S(Bin, Symbol, 0x25C3, "triangleleft"); S(Bin, Symbol, 0x25B9, "triangleright");
            S(Bin, Symbol, 0x228E, "uplus"); S(Bin, Symbol, 0x2A3F, "amalg"); S(Bin, Symbol, 0x2020, "dagger"); S(Bin, Symbol, 0x2021, "ddagger");
            S(Bin, Symbol, 0x22C9, "ltimes"); S(Bin, Symbol, 0x22CA, "rtimes"); S(Bin, Symbol, 0x22CB, "leftthreetimes"); S(Bin, Symbol, 0x22CC, "rightthreetimes");
            S(Bin, Symbol, 0x229E, "boxplus"); S(Bin, Symbol, 0x229F, "boxminus"); S(Bin, Symbol, 0x22A0, "boxtimes"); S(Bin, Symbol, 0x22A1, "boxdot");
            S(Bin, Symbol, 0x2214, "dotplus"); S(Bin, Symbol, 0x22C7, "divideontimes"); S(Bin, Symbol, 0x22BA, "intercal"); S(Bin, Symbol, 0x22BC, "barwedge");
            S(Bin, Symbol, 0x22BB, "veebar"); S(Bin, Symbol, 0x22B2, "lhd"); S(Bin, Symbol, 0x22B3, "rhd"); S(Bin, Symbol, 0x22B4, "unlhd");
            S(Bin, Symbol, 0x22B5, "unrhd");

            S(Rel, Symbol, 0x2264, "leq le"); S(Rel, Symbol, 0x2265, "geq ge"); S(Rel, Symbol, 0x2260, "neq ne"); S(Rel, Symbol, 0x2266, "leqq");
            S(Rel, Symbol, 0x2267, "geqq"); S(Rel, Symbol, 0x2A7D, "leqslant"); S(Rel, Symbol, 0x2A7E, "geqslant"); S(Rel, Symbol, 0x2A87, "lneq");
            S(Rel, Symbol, 0x2A88, "gneq"); S(Rel, Symbol, 0x2268, "lneqq"); S(Rel, Symbol, 0x2269, "gneqq"); S(Rel, Symbol, 0x226A, "ll");
            S(Rel, Symbol, 0x226B, "gg"); S(Rel, Symbol, 0x22D8, "lll llless"); S(Rel, Symbol, 0x22D9, "ggg gggtr"); S(Rel, Symbol, 0x226E, "nless");
            S(Rel, Symbol, 0x226F, "ngtr"); S(Rel, Symbol, 0x2270, "nleq"); S(Rel, Symbol, 0x2271, "ngeq"); S(Rel, Symbol, 0x227A, "prec");
            S(Rel, Symbol, 0x227B, "succ"); S(Rel, Symbol, 0x2AAF, "preceq"); S(Rel, Symbol, 0x2AB0, "succeq"); S(Rel, Symbol, 0x2280, "nprec");
            S(Rel, Symbol, 0x2281, "nsucc"); S(Rel, Symbol, 0x223C, "sim thicksim"); S(Rel, Symbol, 0x2241, "nsim"); S(Rel, Symbol, 0x2243, "simeq");
            S(Rel, Symbol, 0x2248, "approx thickapprox"); S(Rel, Symbol, 0x2249, "napprox"); S(Rel, Symbol, 0x224A, "approxeq"); S(Rel, Symbol, 0x2245, "cong");
            S(Rel, Symbol, 0x2247, "ncong"); S(Rel, Symbol, 0x2261, "equiv"); S(Rel, Symbol, 0x2262, "nequiv"); S(Rel, Symbol, 0x221D, "propto varpropto");
            S(Rel, Symbol, 0x2250, "doteq"); S(Rel, Symbol, 0x2251, "doteqdot Doteq"); S(Rel, Symbol, 0x224D, "asymp"); S(Rel, Symbol, 0x22A8, "models vDash");
            S(Rel, Symbol, 0x22A2, "vdash"); S(Rel, Symbol, 0x22A3, "dashv"); S(Rel, Symbol, 0x22A9, "Vdash"); S(Rel, Symbol, 0x22AC, "nvdash");
            S(Rel, Symbol, 0x22AD, "nvDash"); S(Rel, Symbol, 0x22A5, "perp"); S(Rel, Symbol, 0x2223, "mid shortmid"); S(Rel, Symbol, 0x2224, "nmid nshortmid");
            S(Rel, Symbol, 0x2225, "parallel shortparallel"); S(Rel, Symbol, 0x2226, "nparallel nshortparallel"); S(Rel, Symbol, 0x2282, "subset");
            S(Rel, Symbol, 0x2283, "supset"); S(Rel, Symbol, 0x2286, "subseteq"); S(Rel, Symbol, 0x2287, "supseteq"); S(Rel, Symbol, 0x228A, "subsetneq varsubsetneq");
            S(Rel, Symbol, 0x228B, "supsetneq varsupsetneq"); S(Rel, Symbol, 0x2288, "nsubseteq"); S(Rel, Symbol, 0x2289, "nsupseteq");
            S(Rel, Symbol, 0x2284, "nsubset"); S(Rel, Symbol, 0x2285, "nsupset"); S(Rel, Symbol, 0x2AC5, "subseteqq"); S(Rel, Symbol, 0x2AC6, "supseteqq");
            S(Rel, Symbol, 0x22D0, "Subset"); S(Rel, Symbol, 0x22D1, "Supset"); S(Rel, Symbol, 0x228F, "sqsubset"); S(Rel, Symbol, 0x2290, "sqsupset");
            S(Rel, Symbol, 0x2291, "sqsubseteq"); S(Rel, Symbol, 0x2292, "sqsupseteq"); S(Rel, Symbol, 0x2208, "in isin"); S(Rel, Symbol, 0x220B, "ni owns");
            S(Rel, Symbol, 0x2209, "notin"); S(Rel, Symbol, 0x220C, "notni"); S(Rel, Symbol, 0x2323, "smile"); S(Rel, Symbol, 0x2322, "frown");
            S(Rel, Symbol, 0x2272, "lesssim"); S(Rel, Symbol, 0x2273, "gtrsim"); S(Rel, Symbol, 0x2A85, "lessapprox"); S(Rel, Symbol, 0x2A86, "gtrapprox");
            S(Rel, Symbol, 0x2276, "lessgtr"); S(Rel, Symbol, 0x2277, "gtrless"); S(Rel, Symbol, 0x22DA, "lesseqgtr"); S(Rel, Symbol, 0x22DB, "gtreqless");
            S(Rel, Symbol, 0x225C, "triangleq"); S(Rel, Symbol, 0x2254, "coloneqq coloneq Coloneqq"); S(Rel, Symbol, 0x2255, "eqqcolon eqcolon");
            S(Rel, Symbol, 0x224F, "bumpeq"); S(Rel, Symbol, 0x224E, "Bumpeq"); S(Rel, Symbol, 0x2257, "circeq"); S(Rel, Symbol, 0x2256, "eqcirc");
            S(Rel, Symbol, 0x2253, "risingdotseq"); S(Rel, Symbol, 0x2252, "fallingdotseq"); S(Rel, Symbol, 0x223D, "backsim"); S(Rel, Symbol, 0x22CD, "backsimeq");
            S(Rel, Symbol, 0x2242, "eqsim"); S(Rel, Symbol, 0x22B2, "vartriangleleft"); S(Rel, Symbol, 0x22B3, "vartriangleright");
            S(Rel, Symbol, 0x22B4, "trianglelefteq"); S(Rel, Symbol, 0x22B5, "trianglerighteq"); S(Rel, Symbol, 0x22EA, "ntriangleleft");
            S(Rel, Symbol, 0x22EB, "ntriangleright"); S(Rel, Symbol, 0x226C, "between"); S(Rel, Symbol, 0x22D4, "pitchfork");
            S(Rel, Symbol, 0x2234, "therefore"); S(Rel, Symbol, 0x2235, "because"); S(Rel, Symbol, 0x22C8, "bowtie Join"); S(Rel, Symbol, 0x22B8, "multimap");
            S(Rel, Symbol, 0x227E, "precsim"); S(Rel, Symbol, 0x227F, "succsim"); S(Rel, Symbol, 0x2AB7, "precapprox"); S(Rel, Symbol, 0x2AB8, "succapprox");
            S(Rel, Symbol, 0x22DE, "curlyeqprec"); S(Rel, Symbol, 0x22DF, "curlyeqsucc"); S(Rel, Symbol, 0x227C, "preccurlyeq"); S(Rel, Symbol, 0x227D, "succcurlyeq");
            S(Rel, Symbol, 0x2244, "nsimeq");

            S(Rel, Symbol, 0x2192, "to rightarrow"); S(Rel, Symbol, 0x2190, "gets leftarrow"); S(Rel, Symbol, 0x2194, "leftrightarrow");
            S(Rel, Symbol, 0x21D2, "Rightarrow"); S(Rel, Symbol, 0x21D0, "Leftarrow"); S(Rel, Symbol, 0x21D4, "Leftrightarrow");
            S(Rel, Symbol, 0x27F6, "longrightarrow"); S(Rel, Symbol, 0x27F5, "longleftarrow"); S(Rel, Symbol, 0x27F7, "longleftrightarrow");
            S(Rel, Symbol, 0x27F9, "Longrightarrow"); S(Rel, Symbol, 0x27F8, "Longleftarrow"); S(Rel, Symbol, 0x27FA, "Longleftrightarrow");
            S(Rel, Symbol, 0x21A6, "mapsto"); S(Rel, Symbol, 0x27FC, "longmapsto"); S(Rel, Symbol, 0x2191, "uparrow"); S(Rel, Symbol, 0x2193, "downarrow");
            S(Rel, Symbol, 0x2195, "updownarrow"); S(Rel, Symbol, 0x21D1, "Uparrow"); S(Rel, Symbol, 0x21D3, "Downarrow"); S(Rel, Symbol, 0x21D5, "Updownarrow");
            S(Rel, Symbol, 0x2197, "nearrow"); S(Rel, Symbol, 0x2198, "searrow"); S(Rel, Symbol, 0x2199, "swarrow"); S(Rel, Symbol, 0x2196, "nwarrow");
            S(Rel, Symbol, 0x21AA, "hookrightarrow"); S(Rel, Symbol, 0x21A9, "hookleftarrow"); S(Rel, Symbol, 0x21BC, "leftharpoonup");
            S(Rel, Symbol, 0x21C0, "rightharpoonup"); S(Rel, Symbol, 0x21BD, "leftharpoondown"); S(Rel, Symbol, 0x21C1, "rightharpoondown");
            S(Rel, Symbol, 0x21BF, "upharpoonleft"); S(Rel, Symbol, 0x21BE, "upharpoonright restriction"); S(Rel, Symbol, 0x21C3, "downharpoonleft");
            S(Rel, Symbol, 0x21C2, "downharpoonright"); S(Rel, Symbol, 0x21CC, "rightleftharpoons"); S(Rel, Symbol, 0x21CB, "leftrightharpoons");
            S(Rel, Symbol, 0x21C4, "rightleftarrows"); S(Rel, Symbol, 0x21C6, "leftrightarrows"); S(Rel, Symbol, 0x21C9, "rightrightarrows");
            S(Rel, Symbol, 0x21C7, "leftleftarrows"); S(Rel, Symbol, 0x21C8, "upuparrows"); S(Rel, Symbol, 0x21CA, "downdownarrows");
            S(Rel, Symbol, 0x21A0, "twoheadrightarrow"); S(Rel, Symbol, 0x219E, "twoheadleftarrow"); S(Rel, Symbol, 0x21A3, "rightarrowtail");
            S(Rel, Symbol, 0x21A2, "leftarrowtail"); S(Rel, Symbol, 0x21AC, "looparrowright"); S(Rel, Symbol, 0x21AB, "looparrowleft");
            S(Rel, Symbol, 0x21DD, "rightsquigarrow leadsto"); S(Rel, Symbol, 0x21AD, "leftrightsquigarrow"); S(Rel, Symbol, 0x21BA, "circlearrowleft");
            S(Rel, Symbol, 0x21BB, "circlearrowright"); S(Rel, Symbol, 0x21B6, "curvearrowleft"); S(Rel, Symbol, 0x21B7, "curvearrowright");
            S(Rel, Symbol, 0x219B, "nrightarrow"); S(Rel, Symbol, 0x219A, "nleftarrow"); S(Rel, Symbol, 0x21CF, "nRightarrow"); S(Rel, Symbol, 0x21CD, "nLeftarrow");
            S(Rel, Symbol, 0x21AE, "nleftrightarrow"); S(Rel, Symbol, 0x21CE, "nLeftrightarrow"); S(Rel, Symbol, 0x21B0, "Lsh"); S(Rel, Symbol, 0x21B1, "Rsh");
            S(Rel, Symbol, 0x21E2, "dashrightarrow"); S(Rel, Symbol, 0x21E0, "dashleftarrow"); S(Rel, Symbol, 0x21DB, "Rrightarrow"); S(Rel, Symbol, 0x21DA, "Lleftarrow");

            Big(0x2211, 0, "sum"); Big(0x220F, 0, "prod"); Big(0x2210, 0, "coprod"); Big(0x22C3, 0, "bigcup"); Big(0x22C2, 0, "bigcap");
            Big(0x2A06, 0, "bigsqcup"); Big(0x22C1, 0, "bigvee"); Big(0x22C0, 0, "bigwedge"); Big(0x2A01, 0, "bigoplus"); Big(0x2A02, 0, "bigotimes");
            Big(0x2A00, 0, "bigodot"); Big(0x2A04, 0, "biguplus");
            Big(0x222B, -1, "int intop smallint"); Big(0x222C, -1, "iint"); Big(0x222D, -1, "iiint"); Big(0x2A0C, -1, "iiiint");
            Big(0x222E, -1, "oint"); Big(0x222F, -1, "oiint"); Big(0x2230, -1, "oiiint");

            S(Open, Symbol, 0x27E8, "langle lang"); S(Close, Symbol, 0x27E9, "rangle rang"); S(Open, Symbol, 0x2308, "lceil");
            S(Close, Symbol, 0x2309, "rceil"); S(Open, Symbol, 0x230A, "lfloor"); S(Close, Symbol, 0x230B, "rfloor");
            S(Open, Symbol, 0x27EE, "lgroup"); S(Close, Symbol, 0x27EF, "rgroup"); S(Open, Symbol, 0x27E6, "llbracket"); S(Close, Symbol, 0x27E7, "rrbracket");
            S(Open, Symbol, 0x231C, "ulcorner"); S(Close, Symbol, 0x231D, "urcorner"); S(Open, Symbol, 0x231E, "llcorner"); S(Close, Symbol, 0x231F, "lrcorner");
            S(Open, Roman, '{', "lbrace"); S(Close, Roman, '}', "rbrace"); S(Open, Roman, '[', "lbrack"); S(Close, Roman, ']', "rbrack");
            S(Open, Symbol, '|', "lvert"); S(Close, Symbol, '|', "rvert"); S(Open, Symbol, 0x2016, "lVert"); S(Close, Symbol, 0x2016, "rVert");
            S(Punct, Roman, ':', "colon"); S(Punct, Roman, '.', "ldotp"); S(Punct, Symbol, 0x22C5, "cdotp");

            Fn("arcsin arccos arctan arctg arcctg arg ch cos cosec cosh cot cotg coth csc ctg cth deg dim exp hom ker lg ln log sec sin sinh sh tan tanh tg th", false);
            Fn("det gcd inf lim liminf limsup max min Pr sup", true);
            FnAs("liminf", "lim inf", true); FnAs("limsup", "lim sup", true); FnAs("argmax", "arg max", true); FnAs("argmin", "arg min", true);
            FnAs("injlim", "inj lim", true); FnAs("projlim", "proj lim", true); FnAs("lcm", "lcm", false); FnAs("sgn", "sgn", false);
            FnAs("Var", "Var", false); FnAs("Cov", "Cov", false); FnAs("tr", "tr", false); FnAs("Tr", "Tr", false); FnAs("rank", "rank", false);
            FnAs("diag", "diag", false); FnAs("erf", "erf", false); FnAs("sign", "sign", false); FnAs("span", "span", false);

            Neg('=', 0x2260); Neg('<', 0x226E); Neg('>', 0x226F); Neg(0x2208, 0x2209); Neg(0x2261, 0x2262); Neg(0x2282, 0x2284);
            Neg(0x2283, 0x2285); Neg(0x2286, 0x2288); Neg(0x2287, 0x2289); Neg(0x2264, 0x2270); Neg(0x2265, 0x2271); Neg(0x223C, 0x2241);
            Neg(0x2248, 0x2249); Neg(0x2245, 0x2247); Neg(0x2223, 0x2224); Neg(0x2225, 0x2226); Neg(0x2203, 0x2204); Neg(0x220B, 0x220C);
            Neg(0x227A, 0x2280); Neg(0x227B, 0x2281); Neg(0x2243, 0x2244); Neg(0x22A2, 0x22AC); Neg(0x22A8, 0x22AD); Neg(0x2192, 0x219B);
            Neg(0x2190, 0x219A); Neg(0x2194, 0x21AE); Neg(0x21D2, 0x21CF); Neg(0x21D0, 0x21CD); Neg(0x21D4, 0x21CE);
        }

        public static bool Integral(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char c = s[0];
            return (c >= 0x222B && c <= 0x2233) || c == 0x2A0C;
        }
    }

    class MNode
    {
        public const int Sym = 0, Group = 1, Script = 2, Frac = 3, Sqrt = 4, Fence = 5, Accent = 6, Txt = 7, Space = 8, Array = 9,
            Boxed = 10, Stack = 11, Delim = 12, Phantom = 13, Rule = 14, XArrow = 15, Cancel = 16, Sty = 17;
        public const int WideRel = 1, OpName = 2;
        public int Kind, Cls, Face = Tex.Roman, Limits, Style = -1, Flags;
        public string Text, L, R, Spec;
        public MNode A, B, C;
        public List<MNode> List;
        public List<List<MNode>> Rows;
        public List<int> Lines;
        public double Size, Size2, Size3;
        public bool Big;
    }

    class TexParser
    {
        string s;
        int p, nodes, depth;
        int face = -1, variant;
        bool opname, alpha;
        public bool Overflow;
        public string Tag;

        const int MaxNodes = 6000, MaxDepth = 40, MaxCells = 2500;
        const int SBrace = 1, SRight = 2, SEnd = 4, SCell = 8, SBracket = 16, SRow = 32;
        public const int VarBB = 1, VarCal = 2, VarFrak = 3, VarScr = 4;

        public static MNode Parse(string tex, bool mathAlphabets, out string tag, out bool overflow)
        {
            TexParser t = new TexParser();
            t.s = tex ?? "";
            t.alpha = mathAlphabets;
            MNode root = null;
            try { root = t.Body(); }
            catch (Exception) { t.Overflow = true; }
            tag = t.Tag;
            overflow = t.Overflow;
            return root;
        }

        MNode N(int kind)
        {
            if (++nodes > MaxNodes) Overflow = true;
            MNode n = new MNode();
            n.Kind = kind;
            return n;
        }

        MNode Group(List<MNode> l)
        {
            MNode g = N(MNode.Group);
            g.List = l ?? new List<MNode>();
            g.Cls = Tex.Ord;
            return g;
        }

        MNode Empty() { return Group(new List<MNode>()); }

        MNode Sym(string text, int cls, int f)
        {
            MNode n = N(MNode.Sym);
            n.Text = text;
            n.Cls = cls;
            n.Face = f;
            return n;
        }

        MNode Space(double em)
        {
            MNode n = N(MNode.Space);
            n.Size = Math.Max(-3, Math.Min(10, em));
            n.Cls = -1;
            return n;
        }

        static bool Alpha(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }

        void Skip()
        {
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
        }

        string PeekName()
        {
            if (p >= s.Length || s[p] != '\\' || p + 1 >= s.Length) return null;
            int j = p + 1;
            if (!Alpha(s[j])) return s.Substring(j, 1);
            while (j < s.Length && Alpha(s[j])) j++;
            return s.Substring(p + 1, j - p - 1);
        }

        MNode Body()
        {
            int why;
            bool amp;
            List<int> lines;
            List<List<List<MNode>>> rows = Rows(0, out why, out amp, out lines);
            if (rows.Count == 1 && rows[0].Count == 1) return Group(rows[0][0]);
            return MakeArray(amp ? 2 : 3, rows, lines, null, null, null, -1);
        }

        List<List<List<MNode>>> Rows(int stop, out int why, out bool amp, out List<int> lines)
        {
            List<List<List<MNode>>> rows = new List<List<List<MNode>>>();
            List<List<MNode>> row = new List<List<MNode>>();
            lines = null;
            amp = false;
            int cells = 0;
            while (true)
            {
                if (row.Count == 0) Hlines(rows.Count, ref lines);
                List<MNode> cell = Seq(stop | SCell | SRow, out why);
                row.Add(cell);
                if (++cells > MaxCells) Overflow = true;
                if (Overflow) { rows.Add(row); break; }
                if (why == SCell) { p++; amp = true; continue; }
                if (why == SRow)
                {
                    SkipRowBreak();
                    rows.Add(row);
                    row = new List<List<MNode>>();
                    continue;
                }
                rows.Add(row);
                break;
            }
            if (rows.Count > 1)
            {
                List<List<MNode>> last = rows[rows.Count - 1];
                if (last.Count == 1 && last[0].Count == 0) rows.RemoveAt(rows.Count - 1);
            }
            return rows;
        }

        void Hlines(int row, ref List<int> lines)
        {
            while (true)
            {
                Skip();
                string n = PeekName();
                if (n != "hline" && n != "hdashline" && n != "midrule" && n != "toprule" && n != "bottomrule") return;
                p += 1 + n.Length;
                if (lines == null) lines = new List<int>();
                lines.Add(row);
            }
        }

        void SkipRowBreak()
        {
            string n = PeekName();
            if (n == null) return;
            p += 1 + n.Length;
            if (p < s.Length && s[p] == '*') p++;
            int save = p;
            Skip();
            if (p < s.Length && s[p] == '[')
            {
                int close = s.IndexOf(']', p);
                if (close > p && close - p < 40) { p = close + 1; return; }
            }
            p = save;
        }

        List<MNode> Seq(int stop, out int why)
        {
            List<MNode> list = new List<MNode>();
            why = 0;
            if (++depth > MaxDepth) { Overflow = true; depth--; return list; }
            while (!Overflow)
            {
                Skip();
                if (p >= s.Length) break;
                char c = s[p];
                if (c == '}') { if ((stop & SBrace) != 0) { why = SBrace; break; } p++; continue; }
                if (c == ']' && (stop & SBracket) != 0) { why = SBracket; break; }
                if (c == '&') { if ((stop & SCell) != 0) { why = SCell; break; } p++; continue; }
                if (c == '\\')
                {
                    string name = PeekName();
                    if (name == null) { p++; continue; }
                    if (name == "\\" || name == "cr" || name == "newline")
                    {
                        if ((stop & SRow) != 0) { why = SRow; break; }
                        p += 1 + name.Length;
                        continue;
                    }
                    if (name == "right" && (stop & SRight) != 0) { why = SRight; break; }
                    if (name == "end" && (stop & SEnd) != 0) { why = SEnd; break; }
                    if (name == "over" || name == "choose" || name == "atop" || name == "brace" || name == "brack")
                    {
                        p += 1 + name.Length;
                        int w2;
                        List<MNode> rest = Seq(stop, out w2);
                        why = w2;
                        MNode f = N(MNode.Frac);
                        f.A = Group(list);
                        f.B = Group(rest);
                        f.Size = name == "over" ? 1 : 0;
                        f.Cls = Tex.Inner;
                        if (name == "choose") { f.L = "("; f.R = ")"; }
                        else if (name == "brace") { f.L = "{"; f.R = "}"; }
                        else if (name == "brack") { f.L = "["; f.R = "]"; }
                        list = new List<MNode>();
                        list.Add(f);
                        break;
                    }
                }
                int before = p;
                MNode a = Atom(stop);
                if (a != null) list.Add(Scripts(a));
                if (p == before) p++;
            }
            depth--;
            return list;
        }

        List<MNode> Braced()
        {
            int sf = face, sv = variant;
            int why;
            List<MNode> l = Seq(SBrace, out why);
            if (why == SBrace) p++;
            face = sf;
            variant = sv;
            return l;
        }

        MNode Atom(int stop)
        {
            char c = s[p];
            switch (c)
            {
                case '{':
                    p++;
                    return Group(Braced());
                case '\\':
                    return Command(stop);
                case '^':
                case '_':
                case '\'':
                    return Empty();
                case '~':
                    p++;
                    return Space(1 / 3.0);
            }
            int cp = c, len = 1;
            if (char.IsHighSurrogate(c) && p + 1 < s.Length && char.IsLowSurrogate(s[p + 1])) { cp = char.ConvertToUtf32(c, s[p + 1]); len = 2; }
            p += len;
            return Char(cp);
        }

        MNode Scripts(MNode b)
        {
            MNode sup = null, sub = null;
            int primes = 0;
            while (!Overflow)
            {
                Skip();
                if (p >= s.Length) break;
                char c = s[p];
                if (c == '^')
                {
                    p++;
                    MNode a = Arg();
                    int pr = PrimeCount(a);
                    if (pr > 0 && sup == null) primes += pr;
                    else sup = Append(sup, a);
                    continue;
                }
                if (c == '_') { p++; MNode a = Arg(); sub = Append(sub, a); continue; }
                if (c == '\'') { p++; primes++; continue; }
                string n = PeekName();
                if (n == "limits" || n == "nolimits" || n == "displaylimits")
                {
                    p += 1 + n.Length;
                    b.Limits = n == "limits" ? 1 : n == "nolimits" ? -1 : 0;
                    continue;
                }
                break;
            }
            if (primes > 0)
            {
                string pr = primes == 1 ? "\u2032" : primes == 2 ? "\u2033" : primes == 3 ? "\u2034" : new string('\u2032', Math.Min(primes, 8));
                List<MNode> l = new List<MNode>();
                l.Add(b);
                l.Add(Sym(pr, Tex.Ord, Tex.Symbol));
                MNode g = Group(l);
                g.Cls = b.Cls == Tex.Op ? Tex.Ord : b.Cls;
                b = g;
            }
            if (sup == null && sub == null) return b;
            MNode n2 = N(MNode.Script);
            n2.A = b;
            n2.B = sup;
            n2.C = sub;
            n2.Cls = b.Cls;
            return n2;
        }

        static int PrimeCount(MNode a)
        {
            if (a == null) return 0;
            if (a.Kind == MNode.Sym && a.Text != null)
            {
                if (a.Text == "\u2032") return 1;
                if (a.Text == "\u2033") return 2;
                if (a.Text == "\u2034") return 3;
                return 0;
            }
            if (a.Kind == MNode.Group && a.List != null && a.List.Count > 0)
            {
                int n = 0;
                foreach (MNode x in a.List)
                {
                    int k = PrimeCount(x);
                    if (k == 0) return 0;
                    n += k;
                }
                return n;
            }
            return 0;
        }

        MNode Append(MNode acc, MNode a)
        {
            if (acc == null) return a;
            if (acc.Kind == MNode.Group && acc.Flags == ScriptRun) { acc.List.Add(a); return acc; }
            MNode g = Pair(acc, a);
            g.Flags = ScriptRun;
            return g;
        }

        const int ScriptRun = 64;

        MNode Pair(MNode a, MNode b)
        {
            List<MNode> l = new List<MNode>();
            l.Add(a);
            l.Add(b);
            return Group(l);
        }

        MNode Arg()
        {
            Skip();
            if (p >= s.Length) return Empty();
            char c = s[p];
            if (c == '{') { p++; return Group(Braced()); }
            if (c == '}' || c == '^' || c == '_' || c == '&' || c == ']') return Empty();
            if (c == '\\')
            {
                string n = PeekName();
                if (n == "\\" || n == "right" || n == "end") return Empty();
                int before = p;
                MNode a = Command(0);
                if (a == null && p == before) p++;
                return a ?? Empty();
            }
            int cp = c, len = 1;
            if (char.IsHighSurrogate(c) && p + 1 < s.Length && char.IsLowSurrogate(s[p + 1])) { cp = char.ConvertToUtf32(c, s[p + 1]); len = 2; }
            p += len;
            if (c == '~') return Space(1 / 3.0);
            return Char(cp) ?? Empty();
        }

        MNode Opt()
        {
            int save = p;
            Skip();
            if (p >= s.Length || s[p] != '[') { p = save; return null; }
            p++;
            int sf = face, sv = variant;
            int why;
            List<MNode> l = Seq(SBracket | SBrace, out why);
            if (why == SBracket) p++;
            face = sf;
            variant = sv;
            return Group(l);
        }

        string BraceRaw()
        {
            Skip();
            if (p >= s.Length) return "";
            if (s[p] == '{')
            {
                int nest = 0, start = p + 1;
                for (int j = p; j < s.Length; j++)
                {
                    char c = s[j];
                    if (c == '\\') { j++; continue; }
                    if (c == '{') nest++;
                    else if (c == '}' && --nest == 0) { p = j + 1; return s.Substring(start, j - start); }
                }
                p = s.Length;
                return s.Substring(start);
            }
            if (s[p] == '\\')
            {
                string n = PeekName();
                if (n == null) { p++; return ""; }
                p += 1 + n.Length;
                return "\\" + n;
            }
            return s[p++].ToString();
        }

        string OptRaw()
        {
            int save = p;
            Skip();
            if (p >= s.Length || s[p] != '[') { p = save; return null; }
            int close = s.IndexOf(']', p);
            if (close < 0) { p = save; return null; }
            string r = s.Substring(p + 1, close - p - 1);
            p = close + 1;
            return r;
        }

        static double Length(string t)
        {
            if (string.IsNullOrEmpty(t)) return 0;
            t = t.Trim();
            int i = 0;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.' || t[i] == '-' || t[i] == '+' || t[i] == ' ')) i++;
            double v;
            if (!double.TryParse(t.Substring(0, i).Replace(" ", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
            string u = t.Substring(i).Trim().ToLowerInvariant();
            if (u.Length > 2) u = u.Substring(0, 2);
            double k;
            switch (u)
            {
                case "em": k = 1; break;
                case "ex": k = 0.43; break;
                case "mu": k = 1 / 18.0; break;
                case "pt": k = 0.1; break;
                case "pc": k = 1.2; break;
                case "mm": k = 0.2845; break;
                case "cm": k = 2.845; break;
                case "in": k = 7.227; break;
                case "bp": k = 0.1004; break;
                case "dd": k = 0.107; break;
                case "cc": k = 1.284; break;
                case "px": k = 0.075; break;
                default: k = 0.1; break;
            }
            return Math.Max(-3, Math.Min(10, v * k));
        }

        double LengthToken()
        {
            Skip();
            if (p < s.Length && s[p] == '{') return Length(BraceRaw());
            int start = p;
            while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.' || s[p] == '-' || s[p] == '+')) p++;
            Skip();
            int u = p;
            while (p < s.Length && p - u < 2 && Alpha(s[p])) p++;
            return Length(s.Substring(start, p - start));
        }

        MNode Char(int cp)
        {
            if (cp < 128)
            {
                char ch = (char)cp;
                if (Alpha(ch)) return Letter(ch);
                if (ch >= '0' && ch <= '9') return Digit(ch);
                switch (ch)
                {
                    case '+': return Sym("+", Tex.Bin, Tex.Symbol);
                    case '-': return opname ? Sym("-", Tex.Ord, Tex.Roman) : Sym("\u2212", Tex.Bin, Tex.Symbol);
                    case '*': return opname ? Sym("*", Tex.Ord, Tex.Roman) : Sym("\u2217", Tex.Bin, Tex.Symbol);
                    case '=':
                    case '<':
                    case '>':
                        return Sym(ch.ToString(), Tex.Rel, Tex.Symbol);
                    case ':': return Sym(":", Tex.Rel, Tex.Roman);
                    case ',':
                    case ';':
                        return Sym(ch.ToString(), Tex.Punct, Tex.Roman);
                    case '!':
                    case '?':
                        return Sym(ch.ToString(), Tex.Close, Tex.Roman);
                    case '(':
                    case '[':
                        return Sym(ch.ToString(), Tex.Open, Tex.Roman);
                    case ')':
                    case ']':
                        return Sym(ch.ToString(), Tex.Close, Tex.Roman);
                    case '|': return Sym("|", Tex.Ord, Tex.Symbol);
                    case '/': return Sym("/", Tex.Ord, Tex.Roman);
                    case '.': return Sym(".", Tex.Ord, Tex.Roman);
                }
                if (ch < ' ' || ch == 127) return null;
                return Sym(ch.ToString(), Tex.Ord, Tex.Roman);
            }
            TexSym t;
            if (Tex.Chars.TryGetValue(cp, out t)) return FromSym(t);
            string str = char.ConvertFromUtf32(cp);
            if (char.IsLetter(str, 0))
            {
                bool upperGreek = cp >= 0x391 && cp <= 0x3A9;
                int f = upperGreek ? Tex.Roman : face >= 0 ? face : Tex.Italic;
                if (cp >= 0x1D400 && cp <= 0x1D7FF) f = Tex.Symbol;
                return Sym(str, Tex.Ord, f);
            }
            if (char.IsDigit(str, 0)) return Sym(str, Tex.Ord, Tex.Roman);
            UnicodeCategory k = CharUnicodeInfo.GetUnicodeCategory(str, 0);
            if (k == UnicodeCategory.Control || k == UnicodeCategory.Format) return null;
            return Sym(str, Tex.Ord, Tex.Symbol);
        }

        MNode Letter(char ch)
        {
            if (variant != 0)
            {
                string v = Variant(ch, variant);
                if (v != null) return Sym(v, Tex.Ord, Tex.Symbol);
                int vf = variant == VarBB ? Tex.Bold : variant == VarFrak ? Tex.Roman : Tex.Italic;
                return Sym(ch.ToString(), Tex.Ord, vf);
            }
            return Sym(ch.ToString(), Tex.Ord, face >= 0 ? face : Tex.Italic);
        }

        MNode Digit(char ch)
        {
            if (variant == VarBB && alpha) return Sym(char.ConvertFromUtf32(0x1D7D8 + (ch - '0')), Tex.Ord, Tex.Symbol);
            int f = face == Tex.BoldItalic ? Tex.Bold : face >= 0 ? face : Tex.Roman;
            return Sym(ch.ToString(), Tex.Ord, f);
        }

        string Variant(char ch, int v)
        {
            bool up = ch >= 'A' && ch <= 'Z';
            int ex = 0;
            switch (v)
            {
                case VarBB:
                    switch (ch) { case 'C': ex = 0x2102; break; case 'H': ex = 0x210D; break; case 'N': ex = 0x2115; break; case 'P': ex = 0x2119; break; case 'Q': ex = 0x211A; break; case 'R': ex = 0x211D; break; case 'Z': ex = 0x2124; break; }
                    if (ex != 0) return ((char)ex).ToString();
                    if (!alpha) return null;
                    return char.ConvertFromUtf32(up ? 0x1D538 + (ch - 'A') : 0x1D552 + (ch - 'a'));
                case VarCal:
                case VarScr:
                    switch (ch) { case 'B': ex = 0x212C; break; case 'E': ex = 0x2130; break; case 'F': ex = 0x2131; break; case 'H': ex = 0x210B; break; case 'I': ex = 0x2110; break; case 'L': ex = 0x2112; break; case 'M': ex = 0x2133; break; case 'R': ex = 0x211B; break; case 'e': ex = 0x212F; break; case 'g': ex = 0x210A; break; case 'o': ex = 0x2134; break; }
                    if (ex != 0) return ((char)ex).ToString();
                    if (!alpha) return null;
                    return char.ConvertFromUtf32(up ? 0x1D49C + (ch - 'A') : 0x1D4B6 + (ch - 'a'));
                case VarFrak:
                    switch (ch) { case 'C': ex = 0x212D; break; case 'H': ex = 0x210C; break; case 'I': ex = 0x2111; break; case 'R': ex = 0x211C; break; case 'Z': ex = 0x2128; break; }
                    if (ex != 0) return ((char)ex).ToString();
                    if (!alpha) return null;
                    return char.ConvertFromUtf32(up ? 0x1D504 + (ch - 'A') : 0x1D51E + (ch - 'a'));
            }
            return null;
        }

        MNode FromSym(TexSym t)
        {
            int f = t.Face;
            if (face >= 0 && (f == Tex.Italic || f == Tex.Roman))
            {
                if (face == Tex.Bold) f = f == Tex.Italic ? Tex.BoldItalic : Tex.Bold;
                else if (face == Tex.BoldItalic) f = t.Cls == Tex.Op ? Tex.Bold : Tex.BoldItalic;
                else if (face == Tex.Roman && t.Text.Length == 1 && t.Text[0] >= 0x391 && t.Text[0] <= 0x3C9) f = Tex.Roman;
            }
            MNode n = Sym(t.Text, t.Cls, f);
            n.Big = t.Big;
            n.Limits = t.Limits;
            if (t.Cls == Tex.Op && !t.Big) n.Flags |= MNode.OpName;
            return n;
        }

        int nest;
        const int MaxNest = 60;

        MNode Command(int stop)
        {
            if (++nest > MaxNest) { Overflow = true; nest--; p = s.Length; return null; }
            try { return CommandBody(stop); }
            finally { nest--; }
        }

        MNode CommandBody(int stop)
        {
            p++;
            if (p >= s.Length) return null;
            char c = s[p];
            string name;
            if (Alpha(c))
            {
                int j = p;
                while (j < s.Length && Alpha(s[j])) j++;
                name = s.Substring(p, j - p);
                p = j;
            }
            else
            {
                name = c.ToString();
                p++;
                if (char.IsHighSurrogate(c) && p < s.Length && char.IsLowSurrogate(s[p])) { name += s[p]; p++; }
                return Punctuation(name);
            }
            TexSym t;
            if (Tex.Cmd.TryGetValue(name, out t)) return FromSym(t);
            switch (name)
            {
                case "frac":
                case "dfrac":
                case "tfrac":
                case "cfrac":
                    {
                        if (name == "cfrac") OptRaw();
                        MNode f = N(MNode.Frac);
                        f.Size = 1;
                        f.Style = name == "dfrac" || name == "cfrac" ? 0 : name == "tfrac" ? 2 : -1;
                        f.A = Arg();
                        f.B = Arg();
                        f.Cls = Tex.Inner;
                        return f;
                    }
                case "binom":
                case "dbinom":
                case "tbinom":
                    {
                        MNode f = N(MNode.Frac);
                        f.Size = 0;
                        f.L = "(";
                        f.R = ")";
                        f.Style = name == "dbinom" ? 0 : name == "tbinom" ? 2 : -1;
                        f.A = Arg();
                        f.B = Arg();
                        f.Cls = Tex.Inner;
                        return f;
                    }
                case "genfrac":
                    {
                        MNode f = N(MNode.Frac);
                        f.L = DelimOf(BraceRaw());
                        f.R = DelimOf(BraceRaw());
                        string th = BraceRaw().Trim();
                        f.Size = th.Length == 0 ? 1 : Length(th) > 0 ? 1 : 0;
                        string st = BraceRaw().Trim();
                        f.Style = st == "0" ? 0 : st == "1" ? 2 : st == "2" ? 4 : st == "3" ? 6 : -1;
                        f.A = Arg();
                        f.B = Arg();
                        f.Cls = Tex.Inner;
                        return f;
                    }
                case "sqrt":
                    {
                        MNode r = N(MNode.Sqrt);
                        r.B = Opt();
                        r.A = Arg();
                        return r;
                    }
                case "left":
                    {
                        string l = DelimTok();
                        int why;
                        List<MNode> body = Seq(SRight | SEnd | SBrace | SCell | SRow, out why);
                        string r = ".";
                        if (why == SRight) { p += 6; r = DelimTok(); }
                        MNode n = N(MNode.Fence);
                        n.A = Group(body);
                        n.L = l;
                        n.R = r;
                        n.Cls = Tex.Inner;
                        return n;
                    }
                case "right":
                    {
                        string d = DelimTok();
                        return d == "." ? null : Sym(d, Tex.Close, DelimFace(d));
                    }
                case "middle":
                    {
                        MNode n = N(MNode.Delim);
                        n.Text = DelimTok();
                        n.Size = -1;
                        n.Cls = Tex.Rel;
                        return n;
                    }
                case "big": case "bigl": case "bigr": case "bigm":
                case "Big": case "Bigl": case "Bigr": case "Bigm":
                case "bigg": case "biggl": case "biggr": case "biggm":
                case "Bigg": case "Biggl": case "Biggr": case "Biggm":
                    {
                        char last = name[name.Length - 1];
                        string bare = last == 'l' || last == 'r' || last == 'm' ? name.Substring(0, name.Length - 1) : name;
                        MNode n = N(MNode.Delim);
                        n.Size = bare == "big" ? 1.2 : bare == "Big" ? 1.8 : bare == "bigg" ? 2.4 : 3.0;
                        n.Text = DelimTok();
                        n.Cls = last == 'l' ? Tex.Open : last == 'r' ? Tex.Close : last == 'm' ? Tex.Rel : Tex.Ord;
                        return n;
                    }
                case "hat": case "widehat": case "check": case "widecheck": case "tilde": case "widetilde":
                case "acute": case "grave": case "dot": case "ddot": case "dddot": case "ddddot": case "breve":
                case "bar": case "vec": case "mathring": case "overline": case "underline": case "overrightarrow":
                case "overleftarrow": case "overleftrightarrow": case "underrightarrow": case "underleftarrow":
                case "underleftrightarrow": case "Overrightarrow": case "utilde": case "overgroup": case "undergroup":
                case "overlinesegment": case "underlinesegment": case "wideparen": case "uline":
                    {
                        MNode a = N(MNode.Accent);
                        a.Text = name;
                        a.A = Arg();
                        return a;
                    }
                case "overbrace":
                case "underbrace":
                case "overbracket":
                case "underbracket":
                    {
                        MNode a = N(MNode.Accent);
                        a.Text = name.EndsWith("bracket") ? (name.StartsWith("over") ? "overbrace" : "underbrace") : name;
                        a.A = Arg();
                        a.Cls = Tex.Op;
                        a.Limits = 1;
                        return a;
                    }
                case "not":
                    {
                        Skip();
                        if (p >= s.Length) return Sym("/", Tex.Rel, Tex.Symbol);
                        MNode next = Atom(stop);
                        if (next == null) return null;
                        if (next.Kind == MNode.Sym && !string.IsNullOrEmpty(next.Text))
                        {
                            int neg;
                            int cp = char.ConvertToUtf32(next.Text, 0);
                            if (next.Text.Length <= 2 && Tex.Negated.TryGetValue(cp, out neg)) next.Text = char.ConvertFromUtf32(neg);
                            else next.Text += "\u0338";
                            next.Cls = Tex.Rel;
                            next.Face = Tex.Symbol;
                        }
                        return next;
                    }
                case "text": case "textrm": case "textnormal": case "textup": case "textmd": case "mbox": case "hbox": case "textsc":
                    return TextArg(Tex.Roman);
                case "textbf":
                    return TextArg(Tex.Bold);
                case "textit":
                case "textsl":
                case "emph":
                    return TextArg(Tex.Italic);
                case "texttt":
                    return TextArg(Tex.Mono);
                case "textsf":
                    return TextArg(Tex.Sans);
                case "fbox":
                case "framebox":
                    {
                        if (name == "framebox") { OptRaw(); OptRaw(); }
                        MNode b = N(MNode.Boxed);
                        b.A = TextArg(Tex.Roman);
                        return b;
                    }
                case "operatorname":
                case "operatornamewithlimits":
                    {
                        bool lim = name != "operatorname";
                        if (p < s.Length && s[p] == '*') { p++; lim = true; }
                        int sf = face, sv = variant;
                        bool so = opname;
                        face = Tex.Roman;
                        variant = 0;
                        opname = true;
                        MNode a = Arg();
                        face = sf;
                        variant = sv;
                        opname = so;
                        MNode g = a.Kind == MNode.Group ? a : Pair(a, Empty());
                        g.Cls = Tex.Op;
                        g.Limits = lim ? 0 : -1;
                        g.Flags |= MNode.OpName;
                        return g;
                    }
                case "mathrm": case "mathup": case "rm": return name == "rm" ? Switch(Tex.Roman, 0) : FontArg(Tex.Roman, 0);
                case "mathit": case "it": return name == "it" ? Switch(Tex.Italic, 0) : FontArg(Tex.Italic, 0);
                case "mathbf": case "bf": case "bold": case "mathbfup": return name == "bf" ? Switch(Tex.Bold, 0) : FontArg(Tex.Bold, 0);
                case "boldsymbol": case "bm": case "pmb": case "mathbfit": case "boldmath": return name == "boldmath" ? Switch(Tex.BoldItalic, 0) : FontArg(Tex.BoldItalic, 0);
                case "mathsf": case "sf": case "mathsfup": return name == "sf" ? Switch(Tex.Sans, 0) : FontArg(Tex.Sans, 0);
                case "mathtt": case "tt": return name == "tt" ? Switch(Tex.Mono, 0) : FontArg(Tex.Mono, 0);
                case "mathbb": case "Bbb": case "mathds": case "mathbbm": case "mathbbb": return FontArg(-2, VarBB);
                case "mathcal": case "cal": case "mathcalligra": return name == "cal" ? Switch(-2, VarCal) : FontArg(-2, VarCal);
                case "mathscr": case "scr": return FontArg(-2, VarScr);
                case "mathfrak": case "frak": case "Frak": return FontArg(-2, VarFrak);
                case "mathnormal": case "mit": return name == "mit" ? Switch(-1, 0) : FontArg(-1, 0);
                case "begin":
                    return Env();
                case "end":
                    BraceRaw();
                    return null;
                case "displaystyle": return StyleNode(0);
                case "textstyle": return StyleNode(2);
                case "scriptstyle": return StyleNode(4);
                case "scriptscriptstyle": return StyleNode(6);
                case "quad": return Space(1);
                case "qquad": return Space(2);
                case "enspace": case "enskip": return Space(0.5);
                case "thinspace": return Space(3 / 18.0);
                case "medspace": return Space(4 / 18.0);
                case "thickspace": return Space(5 / 18.0);
                case "negthinspace": return Space(-3 / 18.0);
                case "negmedspace": return Space(-4 / 18.0);
                case "negthickspace": return Space(-5 / 18.0);
                case "nobreakspace": case "space": return Space(1 / 3.0);
                case "hfill": case "hfil": case "hss": return Space(1);
                case "hspace":
                    if (p < s.Length && s[p] == '*') p++;
                    return Space(Length(BraceRaw()));
                case "hskip": case "kern": case "mkern": case "mskip": case "mspace": case "hkern":
                    return Space(LengthToken());
                case "boxed":
                    {
                        MNode b = N(MNode.Boxed);
                        b.A = Arg();
                        return b;
                    }
                case "color":
                    BraceRaw();
                    return null;
                case "textcolor":
                    BraceRaw();
                    return Arg();
                case "colorbox":
                    BraceRaw();
                    return TextArg(Tex.Roman);
                case "fcolorbox":
                    BraceRaw();
                    BraceRaw();
                    return TextArg(Tex.Roman);
                case "cancel": case "bcancel": case "xcancel": case "sout":
                    {
                        MNode cn = N(MNode.Cancel);
                        cn.Text = name;
                        cn.A = Arg();
                        return cn;
                    }
                case "cancelto":
                    {
                        BraceRaw();
                        MNode cn = N(MNode.Cancel);
                        cn.Text = "cancel";
                        cn.A = Arg();
                        return cn;
                    }
                case "overset":
                case "stackrel":
                case "underset":
                    {
                        MNode st = N(MNode.Stack);
                        if (name == "underset") st.C = Arg(); else st.B = Arg();
                        st.A = Arg();
                        st.Cls = name == "stackrel" ? Tex.Rel : ClassOf(st.A);
                        return st;
                    }
                case "xrightarrow": case "xleftarrow": case "xRightarrow": case "xLeftarrow": case "xleftrightarrow":
                case "xLeftrightarrow": case "xmapsto": case "xlongequal": case "xhookrightarrow": case "xhookleftarrow":
                case "xtwoheadrightarrow": case "xtwoheadleftarrow": case "xrightharpoonup": case "xrightharpoondown":
                case "xleftharpoonup": case "xleftharpoondown": case "xrightleftharpoons": case "xleftrightharpoons": case "xtofrom":
                    {
                        MNode x = N(MNode.XArrow);
                        x.Text = name.Substring(1);
                        x.B = Opt();
                        x.A = Arg();
                        x.Cls = Tex.Rel;
                        return x;
                    }
                case "pmod":
                case "pod":
                    {
                        MNode arg = Arg();
                        List<MNode> l = new List<MNode>();
                        l.Add(Space(1));
                        l.Add(Sym("(", Tex.Open, Tex.Roman));
                        if (name == "pmod") { l.Add(Sym("mod", Tex.Ord, Tex.Roman)); l.Add(Space(6 / 18.0)); }
                        l.Add(arg);
                        l.Add(Sym(")", Tex.Close, Tex.Roman));
                        return Group(l);
                    }
                case "mod":
                    {
                        List<MNode> l = new List<MNode>();
                        l.Add(Space(1));
                        l.Add(Sym("mod", Tex.Ord, Tex.Roman));
                        l.Add(Space(6 / 18.0));
                        l.Add(Arg());
                        return Group(l);
                    }
                case "bmod":
                    return Sym("mod", Tex.Bin, Tex.Roman);
                case "tag":
                    {
                        bool star = p < s.Length && s[p] == '*';
                        if (star) p++;
                        string raw = BraceRaw().Trim();
                        if (raw.Length > 0 && raw.Length < 40) Tag = star ? raw : "(" + raw + ")";
                        return null;
                    }
                case "label": case "ref": case "eqref":
                    {
                        string raw = BraceRaw();
                        if (name == "label") return null;
                        MNode tx = N(MNode.Txt);
                        tx.Text = name == "eqref" ? "(" + raw + ")" : raw;
                        tx.Face = Tex.Roman;
                        return tx;
                    }
                case "nonumber": case "notag": case "relax": case "nobreak": case "allowbreak": case "protect": case "centering":
                case "noindent": case "displaybreak": case "medskip": case "smallskip": case "bigskip": case "vfill": case "par":
                case "limits": case "nolimits": case "displaylimits": case "hline": case "hdashline": case "tiny": case "scriptsize":
                case "footnotesize": case "small": case "normalsize": case "large": case "Large": case "LARGE": case "huge": case "Huge":
                case "nolinebreak": case "linebreak": case "pagebreak": case "newpage": case "leavevmode": case "ensuremath":
                    if (name == "displaybreak" || name == "linebreak" || name == "pagebreak") OptRaw();
                    return null;
                case "phantom":
                case "hphantom":
                case "vphantom":
                    {
                        MNode ph = N(MNode.Phantom);
                        ph.Flags = name == "hphantom" ? 1 : name == "vphantom" ? 2 : 3;
                        ph.A = Arg();
                        return ph;
                    }
                case "smash":
                    {
                        OptRaw();
                        MNode ph = N(MNode.Phantom);
                        ph.Flags = 4;
                        ph.A = Arg();
                        return ph;
                    }
                case "mathstrut":
                case "strut":
                    {
                        MNode ph = N(MNode.Phantom);
                        ph.Flags = 2;
                        ph.A = Sym("(", Tex.Open, Tex.Roman);
                        return ph;
                    }
                case "rule":
                    {
                        MNode r = N(MNode.Rule);
                        string raise = OptRaw();
                        r.Size = Math.Max(0, Length(BraceRaw()));
                        r.Size2 = Math.Max(0, Length(BraceRaw()));
                        r.Size3 = raise == null ? 0 : Length(raise);
                        return r;
                    }
                case "mathop": case "mathbin": case "mathrel": case "mathord": case "mathopen": case "mathclose": case "mathpunct": case "mathinner":
                    {
                        MNode a = Arg();
                        MNode g = a.Kind == MNode.Group ? a : Pair(a, Empty());
                        if (a.Kind != MNode.Group) g.List.RemoveAt(1);
                        g.Cls = name == "mathop" ? Tex.Op : name == "mathbin" ? Tex.Bin : name == "mathrel" ? Tex.Rel : name == "mathopen" ? Tex.Open
                            : name == "mathclose" ? Tex.Close : name == "mathpunct" ? Tex.Punct : name == "mathinner" ? Tex.Inner : Tex.Ord;
                        if (name == "mathop")
                        {
                            g.Limits = 0;
                            if (g.List.Count == 1 && g.List[0].Kind == MNode.Sym && g.List[0].Big) return g.List[0];
                            g.Flags |= MNode.OpName;
                        }
                        return g;
                    }
                case "substack":
                case "subarray":
                    {
                        string spec = name == "subarray" ? BraceRaw() : "c";
                        Skip();
                        if (p < s.Length && s[p] == '{')
                        {
                            p++;
                            int why;
                            bool amp;
                            List<int> lines;
                            List<List<List<MNode>>> rows = Rows(SBrace, out why, out amp, out lines);
                            if (why == SBrace) p++;
                            return MakeArray(6, rows, lines, spec, null, null, -1);
                        }
                        return Arg();
                    }
                case "abs": return Physics("|", "|");
                case "norm": return Physics("\u2016", "\u2016");
                case "ket": return Physics("|", "\u27E9");
                case "bra": return Physics("\u27E8", "|");
                case "braket": return Physics("\u27E8", "\u27E9");
                case "set": case "Set": return Physics("{", "}");
                case "floor": return Physics("\u230A", "\u230B");
                case "ceil": return Physics("\u2308", "\u2309");
                case "dd":
                    {
                        MNode d = Sym("d", Tex.Ord, Tex.Roman);
                        Skip();
                        if (p < s.Length && s[p] == '{') { List<MNode> l = new List<MNode>(); l.Add(d); l.Add(Arg()); MNode g = Group(l); g.Cls = Tex.Ord; return g; }
                        return d;
                    }
                case "dv":
                case "pdv":
                    {
                        string d = name == "dv" ? "d" : "\u2202";
                        int df = name == "dv" ? Tex.Roman : Tex.Symbol;
                        MNode f1 = Arg();
                        Skip();
                        MNode f = N(MNode.Frac);
                        f.Size = 1;
                        f.Cls = Tex.Inner;
                        if (p < s.Length && s[p] == '{')
                        {
                            MNode x = Arg();
                            f.A = Pair(Sym(d, Tex.Ord, df), f1);
                            f.B = Pair(Sym(d, Tex.Ord, df), x);
                        }
                        else
                        {
                            f.A = Sym(d, Tex.Ord, df);
                            f.B = Pair(Sym(d, Tex.Ord, df), f1);
                        }
                        return f;
                    }
                case "newcommand": case "renewcommand": case "providecommand": case "DeclareMathOperator": case "DeclarePairedDelimiter":
                    {
                        if (p < s.Length && s[p] == '*') p++;
                        BraceRaw();
                        if (name == "DeclarePairedDelimiter") { BraceRaw(); BraceRaw(); return null; }
                        OptRaw();
                        OptRaw();
                        BraceRaw();
                        return null;
                    }
                case "def":
                case "gdef":
                    {
                        BraceRaw();
                        while (p < s.Length && s[p] != '{') p++;
                        BraceRaw();
                        return null;
                    }
                case "let":
                    BraceRaw();
                    Skip();
                    if (p < s.Length && s[p] == '=') p++;
                    BraceRaw();
                    return null;
                case "TeX":
                case "LaTeX":
                    {
                        MNode tx = N(MNode.Txt);
                        tx.Text = name;
                        tx.Face = Tex.Roman;
                        return tx;
                    }
                case "ce":
                case "pu":
                    return Chem(BraceRaw());
                case "char":
                    while (p < s.Length && (char.IsLetterOrDigit(s[p]) || s[p] == '"' || s[p] == '\'')) p++;
                    return null;
                case "iff":
                case "implies":
                case "impliedby":
                    {
                        MNode r = Sym(name == "iff" ? "\u27FA" : name == "implies" ? "\u27F9" : "\u27F8", Tex.Rel, Tex.Symbol);
                        r.Flags |= MNode.WideRel;
                        return r;
                    }
            }
            MNode op = Sym(name, Tex.Op, Tex.Roman);
            op.Limits = -1;
            op.Flags |= MNode.OpName;
            return op;
        }

        MNode Physics(string l, string r)
        {
            if (p < s.Length && s[p] == '*') p++;
            Skip();
            MNode a = Arg();
            if (l == "\u27E8" && r == "\u27E9" && a.Kind == MNode.Group)
            {
                for (int i = 0; i < a.List.Count; i++)
                    if (a.List[i].Kind == MNode.Sym && a.List[i].Text == "|") { a.List[i].Cls = Tex.Rel; a.List[i].Flags = 0; }
            }
            MNode n = N(MNode.Fence);
            n.A = a.Kind == MNode.Group ? a : Pair(a, Empty());
            n.L = l;
            n.R = r;
            n.Cls = Tex.Inner;
            return n;
        }

        MNode Switch(int f, int v)
        {
            if (f != -2) face = f;
            variant = v;
            return null;
        }

        MNode FontArg(int f, int v)
        {
            int sf = face, sv = variant;
            bool so = opname;
            if (f != -2) face = f;
            variant = v;
            MNode a = Arg();
            face = sf;
            variant = sv;
            opname = so;
            return a;
        }

        MNode StyleNode(int st)
        {
            MNode n = N(MNode.Sty);
            n.Style = st;
            n.Cls = -1;
            return n;
        }

        static int ClassOf(MNode n)
        {
            if (n == null) return Tex.Ord;
            if (n.Kind == MNode.Group && n.List != null && n.List.Count == 1) return ClassOf(n.List[0]);
            if (n.Kind == MNode.Sym || n.Kind == MNode.Script) return n.Cls == Tex.Op ? Tex.Op : n.Cls;
            return n.Cls;
        }

        static int DelimFace(string d)
        {
            if (d == "(" || d == ")" || d == "[" || d == "]" || d == "{" || d == "}" || d == "/") return Tex.Roman;
            return Tex.Symbol;
        }

        string DelimOf(string raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return ".";
            TexParser t = new TexParser();
            t.s = raw;
            return t.DelimTok();
        }

        string DelimTok()
        {
            Skip();
            if (p >= s.Length) return ".";
            char c = s[p];
            if (c == '\\')
            {
                int save = p;
                string n = PeekName();
                if (n == null) { p++; return "."; }
                p += 1 + n.Length;
                switch (n)
                {
                    case "{": case "lbrace": case "lBrace": return "{";
                    case "}": case "rbrace": case "rBrace": return "}";
                    case "|": case "Vert": case "lVert": case "rVert": return "\u2016";
                    case "vert": case "lvert": case "rvert": case "mid": return "|";
                    case "langle": case "lang": return "\u27E8";
                    case "rangle": case "rang": return "\u27E9";
                    case "lfloor": return "\u230A";
                    case "rfloor": return "\u230B";
                    case "lceil": return "\u2308";
                    case "rceil": return "\u2309";
                    case "lbrack": return "[";
                    case "rbrack": return "]";
                    case "backslash": return "\\";
                    case "uparrow": return "\u2191";
                    case "downarrow": return "\u2193";
                    case "updownarrow": return "\u2195";
                    case "Uparrow": return "\u21D1";
                    case "Downarrow": return "\u21D3";
                    case "Updownarrow": return "\u21D5";
                    case "lgroup": return "\u27EE";
                    case "rgroup": return "\u27EF";
                    case "llbracket": return "\u27E6";
                    case "rrbracket": return "\u27E7";
                    case "lmoustache": return "\u23B0";
                    case "rmoustache": return "\u23B1";
                }
                p = save;
                return ".";
            }
            switch (c)
            {
                case '(': case ')': case '[': case ']': case '|': case '/':
                case '\u27E8': case '\u27E9': case '\u2016': case '\u230A': case '\u230B': case '\u2308': case '\u2309': case '\u2223':
                    p++;
                    return c == '\u2223' ? "|" : c.ToString();
                case '<': p++; return "\u27E8";
                case '>': p++; return "\u27E9";
                case '.': p++; return ".";
            }
            return ".";
        }

        MNode Punctuation(string name)
        {
            switch (name)
            {
                case ",": return Space(3 / 18.0);
                case ":":
                case ">":
                    return Space(4 / 18.0);
                case ";": return Space(5 / 18.0);
                case "!": return Space(-3 / 18.0);
                case " ": return Space(1 / 3.0);
                case "{": return Sym("{", Tex.Open, Tex.Roman);
                case "}": return Sym("}", Tex.Close, Tex.Roman);
                case "|": return Sym("\u2016", Tex.Ord, Tex.Symbol);
                case "$": case "%": case "&": case "#": case "_":
                    return Sym(name, Tex.Ord, Tex.Roman);
                case "/": case "-": case "'": case "`": case "^": case "\"": case "~": case "=": case ".":
                case "(": case ")": case "[": case "]": case "\\":
                    return null;
            }
            return Sym(name, Tex.Ord, Tex.Roman);
        }

        MNode TextArg(int f)
        {
            if (++nest > MaxNest) { Overflow = true; nest--; p = s.Length; return Empty(); }
            try { return TextBody(f); }
            finally { nest--; }
        }

        MNode TextBody(int f)
        {
            Skip();
            if (p >= s.Length) return Empty();
            List<MNode> parts = new List<MNode>();
            StringBuilder sb = new StringBuilder();
            if (s[p] != '{')
            {
                if (s[p] == '\\') { MNode a = Command(0); return a ?? Empty(); }
                sb.Append(s[p++]);
                return TextNode(sb.ToString(), f);
            }
            p++;
            int braces = 0;
            while (p < s.Length && !Overflow)
            {
                char c = s[p];
                if (c == '}')
                {
                    p++;
                    if (braces == 0) break;
                    braces--;
                    continue;
                }
                if (c == '{') { braces++; p++; continue; }
                if (c == '$' || (c == '\\' && p + 1 < s.Length && s[p + 1] == '('))
                {
                    bool paren = c == '\\';
                    int open = p + (paren ? 2 : 1);
                    bool dbl = !paren && open < s.Length && s[open] == '$';
                    if (dbl) open++;
                    string close = paren ? "\\)" : dbl ? "$$" : "$";
                    int end = s.IndexOf(close, open, StringComparison.Ordinal);
                    if (end > open)
                    {
                        Flush(parts, sb, f);
                        string inner = s.Substring(open, end - open);
                        TexParser sub = new TexParser();
                        sub.s = inner;
                        sub.alpha = alpha;
                        sub.depth = depth + 1;
                        sub.nest = nest;
                        sub.nodes = nodes;
                        int why;
                        List<MNode> l = sub.Seq(0, out why);
                        nodes = sub.nodes;
                        if (sub.Overflow) Overflow = true;
                        parts.Add(Group(l));
                        p = end + close.Length;
                        continue;
                    }
                    p++;
                    sb.Append('$');
                    continue;
                }
                if (c == '\\')
                {
                    string n = PeekName();
                    if (n == null) { p++; continue; }
                    p += 1 + n.Length;
                    switch (n)
                    {
                        case "{": case "}": case "$": case "%": case "&": case "#": case "_": sb.Append(n); continue;
                        case "\\": case " ": case "newline": sb.Append(' '); continue;
                        case ",": case "thinspace": sb.Append('\u2009'); continue;
                        case ";": case ":": case "enspace": sb.Append(' '); continue;
                        case "quad": Flush(parts, sb, f); parts.Add(Space(1)); continue;
                        case "qquad": Flush(parts, sb, f); parts.Add(Space(2)); continue;
                        case "textbf": case "bf": case "bfseries": Flush(parts, sb, f); parts.Add(n == "textbf" ? TextArg(Tex.Bold) : Empty()); continue;
                        case "textit": case "emph": case "textsl": case "itshape": Flush(parts, sb, f); parts.Add(n.StartsWith("text") || n == "emph" ? TextArg(Tex.Italic) : Empty()); continue;
                        case "texttt": Flush(parts, sb, f); parts.Add(TextArg(Tex.Mono)); continue;
                        case "textsf": Flush(parts, sb, f); parts.Add(TextArg(Tex.Sans)); continue;
                        case "text": case "textrm": case "textnormal": case "mbox": case "textup": Flush(parts, sb, f); parts.Add(TextArg(f)); continue;
                        case "ldots": case "dots": case "textellipsis": sb.Append('\u2026'); continue;
                        case "textbackslash": sb.Append('\\'); continue;
                        case "textasciitilde": sb.Append('~'); continue;
                        case "textasciicircum": sb.Append('^'); continue;
                        case "textunderscore": sb.Append('_'); continue;
                        case "textendash": sb.Append('\u2013'); continue;
                        case "textemdash": sb.Append('\u2014'); continue;
                        case "textdegree": case "degree": sb.Append('\u00B0'); continue;
                        case "textquotedblleft": sb.Append('\u201C'); continue;
                        case "textquotedblright": sb.Append('\u201D'); continue;
                        case "textregistered": sb.Append('\u00AE'); continue;
                        case "texttrademark": sb.Append('\u2122'); continue;
                    }
                    TexSym t;
                    if (Tex.Cmd.TryGetValue(n, out t)) { sb.Append(t.Text); continue; }
                    continue;
                }
                if (c == '~') { sb.Append('\u00A0'); p++; continue; }
                if (c == '-' && p + 1 < s.Length && s[p + 1] == '-')
                {
                    bool em = p + 2 < s.Length && s[p + 2] == '-';
                    sb.Append(em ? '\u2014' : '\u2013');
                    p += em ? 3 : 2;
                    continue;
                }
                if (c == '`' && p + 1 < s.Length && s[p + 1] == '`') { sb.Append('\u201C'); p += 2; continue; }
                if (c == '\'' && p + 1 < s.Length && s[p + 1] == '\'') { sb.Append('\u201D'); p += 2; continue; }
                if (c == '\n' || c == '\r' || c == '\t') { sb.Append(' '); p++; continue; }
                sb.Append(c);
                p++;
                if (sb.Length > 4000) Overflow = true;
            }
            Flush(parts, sb, f);
            if (parts.Count == 1) return parts[0];
            return Group(parts);
        }

        MNode TextNode(string text, int f)
        {
            MNode n = N(MNode.Txt);
            n.Text = text;
            n.Face = f;
            return n;
        }

        void Flush(List<MNode> parts, StringBuilder sb, int f)
        {
            if (sb.Length == 0) return;
            parts.Add(TextNode(sb.ToString(), f));
            sb.Length = 0;
        }

        MNode Env()
        {
            string name = BraceRaw().Trim();
            string bare = name.TrimEnd('*');
            string spec = null, l = null, r = null;
            int kind, style = -1;
            switch (bare)
            {
                case "matrix": kind = 0; break;
                case "pmatrix": kind = 0; l = "("; r = ")"; break;
                case "bmatrix": kind = 0; l = "["; r = "]"; break;
                case "Bmatrix": kind = 0; l = "{"; r = "}"; break;
                case "vmatrix": kind = 0; l = "|"; r = "|"; break;
                case "Vmatrix": kind = 0; l = "\u2016"; r = "\u2016"; break;
                case "smallmatrix": kind = 5; break;
                case "cases": kind = 1; l = "{"; r = "."; break;
                case "dcases": kind = 1; l = "{"; r = "."; style = 0; break;
                case "rcases": kind = 1; l = "."; r = "}"; break;
                case "drcases": kind = 1; l = "."; r = "}"; style = 0; break;
                case "aligned": case "align": case "flalign": case "split": case "alignat": case "alignedat": case "xalignat":
                    kind = 2;
                    if (bare == "alignat" || bare == "alignedat" || bare == "xalignat") BraceRaw();
                    break;
                case "eqnarray": kind = 4; spec = "rcl"; style = 0; break;
                case "gathered": case "gather": case "multline": case "equation": case "displaymath": case "math": case "center": case "subequations":
                    kind = 3;
                    break;
                case "array": case "darray": kind = 4; spec = BraceRaw(); if (bare == "darray") style = 0; break;
                case "subarray": kind = 6; spec = BraceRaw(); break;
                default: kind = 0; break;
            }
            if (name.EndsWith("*") && kind == 0) OptRaw();
            if (bare == "aligned" || bare == "gathered" || bare == "array") OptRaw();
            int why;
            bool amp;
            List<int> lines;
            List<List<List<MNode>>> rows = Rows(SEnd, out why, out amp, out lines);
            if (why == SEnd) { p += 4; BraceRaw(); }
            if (kind == 3 && amp) kind = 2;
            if (kind == 3 && rows.Count == 1 && rows[0].Count == 1) return Group(rows[0][0]);
            return MakeArray(kind, rows, lines, spec, l, r, style);
        }

        MNode MakeArray(int kind, List<List<List<MNode>>> rows, List<int> lines, string spec, string l, string r, int style)
        {
            MNode a = N(MNode.Array);
            a.Flags = kind;
            a.L = l;
            a.R = r;
            a.Style = style;
            a.Lines = lines;
            a.Rows = new List<List<MNode>>();
            int cols = 0;
            foreach (List<List<MNode>> row in rows) cols = Math.Max(cols, row.Count);
            StringBuilder al = new StringBuilder(), vl = new StringBuilder();
            if (spec != null)
            {
                int i = 0;
                bool bar = false;
                while (i < spec.Length && al.Length < 64)
                {
                    char ch = spec[i];
                    if (ch == 'l' || ch == 'c' || ch == 'r') { al.Append(ch); vl.Append(bar ? '|' : ' '); bar = false; }
                    else if (ch == 'p' || ch == 'm' || ch == 'b' || ch == 'X') { al.Append('l'); vl.Append(bar ? '|' : ' '); bar = false; int close = spec.IndexOf('}', i); if (close > i) i = close; }
                    else if (ch == '|') bar = true;
                    else if (ch == '@' || ch == '!' || ch == '>' || ch == '<') { int close = spec.IndexOf('}', i); if (close > i) i = close; }
                    i++;
                }
                if (bar) vl.Append('|');
            }
            for (int c = 0; c < cols; c++)
            {
                if (c < al.Length) continue;
                char ch = kind == 1 ? 'l' : kind == 2 ? (c % 2 == 0 ? 'r' : 'l') : 'c';
                al.Append(ch);
                vl.Append(' ');
            }
            a.Spec = al.ToString();
            a.Text = vl.ToString();
            foreach (List<List<MNode>> row in rows)
            {
                List<MNode> cells = new List<MNode>();
                for (int c = 0; c < row.Count; c++)
                {
                    List<MNode> cell = row[c];
                    if (kind == 2 && c % 2 == 1)
                    {
                        List<MNode> withEmpty = new List<MNode>();
                        withEmpty.Add(Empty());
                        withEmpty.AddRange(cell);
                        cell = withEmpty;
                    }
                    cells.Add(Group(cell));
                }
                a.Rows.Add(cells);
            }
            a.Cls = Tex.Ord;
            return a;
        }

        MNode Chem(string raw)
        {
            List<MNode> l = new List<MNode>();
            int i = 0;
            if (raw.Length > 600) raw = raw.Substring(0, 600);
            MNode last = null;
            bool afterSpace = true;
            while (i < raw.Length && !Overflow)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c)) { i++; afterSpace = true; last = null; continue; }
                if (string.CompareOrdinal(raw, i, "<=>", 0, 3) == 0) { l.Add(Sym("\u21CC", Tex.Rel, Tex.Symbol)); i += 3; last = null; continue; }
                if (string.CompareOrdinal(raw, i, "<->", 0, 3) == 0) { l.Add(Sym("\u2194", Tex.Rel, Tex.Symbol)); i += 3; last = null; continue; }
                if (string.CompareOrdinal(raw, i, "->", 0, 2) == 0) { l.Add(Sym("\u2192", Tex.Rel, Tex.Symbol)); i += 2; last = null; continue; }
                if (string.CompareOrdinal(raw, i, "<-", 0, 2) == 0) { l.Add(Sym("\u2190", Tex.Rel, Tex.Symbol)); i += 2; last = null; continue; }
                if (char.IsUpper(c) && c < 128)
                {
                    int j = i + 1;
                    while (j < raw.Length && char.IsLower(raw[j]) && raw[j] < 128 && j - i < 3) j++;
                    last = Sym(raw.Substring(i, j - i), Tex.Ord, Tex.Roman);
                    l.Add(last);
                    i = j;
                    afterSpace = false;
                    continue;
                }
                if (char.IsDigit(c))
                {
                    int j = i;
                    while (j < raw.Length && (char.IsDigit(raw[j]) || raw[j] == '.')) j++;
                    string num = raw.Substring(i, j - i);
                    if (last != null && !afterSpace)
                    {
                        MNode sc = N(MNode.Script);
                        sc.A = last;
                        sc.C = Sym(num, Tex.Ord, Tex.Roman);
                        l[l.Count - 1] = sc;
                        last = null;
                    }
                    else { l.Add(Sym(num, Tex.Ord, Tex.Roman)); last = null; }
                    i = j;
                    afterSpace = false;
                    continue;
                }
                if (c == '^' || c == '_' || ((c == '+' || c == '-') && !afterSpace && l.Count > 0 && (i + 1 >= raw.Length || raw[i + 1] == ' ' || raw[i + 1] == '}')))
                {
                    bool low = c == '_';
                    string ch;
                    if (c == '^' || c == '_')
                    {
                        i++;
                        if (i < raw.Length && raw[i] == '{') { int close = raw.IndexOf('}', i); if (close < 0) close = raw.Length; ch = raw.Substring(i + 1, Math.Max(0, close - i - 1)); i = Math.Min(raw.Length, close + 1); }
                        else { int j = i; while (j < raw.Length && (char.IsDigit(raw[j]) || raw[j] == '+' || raw[j] == '-')) j++; ch = raw.Substring(i, j - i); i = j; }
                    }
                    else { ch = c.ToString(); i++; }
                    MNode script = Sym(ch.Replace('-', '\u2212'), Tex.Ord, Tex.Roman);
                    MNode target = l.Count > 0 && !afterSpace ? l[l.Count - 1] : null;
                    if (target == null)
                    {
                        target = Empty();
                        l.Add(target);
                    }
                    if (target.Kind == MNode.Script && (low ? target.C == null : target.B == null))
                    {
                        if (low) target.C = script;
                        else target.B = script;
                    }
                    else
                    {
                        MNode sc = N(MNode.Script);
                        sc.A = target;
                        if (low) sc.C = script;
                        else sc.B = script;
                        l[l.Count - 1] = sc;
                    }
                    afterSpace = false;
                    last = null;
                    continue;
                }
                if (c == '+') { l.Add(Sym("+", Tex.Bin, Tex.Symbol)); i++; last = null; continue; }
                if (c == '=') { l.Add(Sym("=", Tex.Rel, Tex.Symbol)); i++; last = null; continue; }
                if (c == '(' || c == '[') { l.Add(Sym(c.ToString(), Tex.Open, Tex.Roman)); i++; last = null; afterSpace = false; continue; }
                if (c == ')' || c == ']') { last = Sym(c.ToString(), Tex.Close, Tex.Roman); l.Add(last); i++; afterSpace = false; continue; }
                if (c == '\\') { int j = i + 1; while (j < raw.Length && Alpha(raw[j])) j++; string n = raw.Substring(i + 1, j - i - 1); TexSym t; if (Tex.Cmd.TryGetValue(n, out t)) l.Add(FromSym(t)); i = Math.Max(j, i + 1); last = null; continue; }
                l.Add(Sym(c == '-' ? "\u2212" : c.ToString(), c == '-' ? Tex.Bin : Tex.Ord, Tex.Roman));
                last = null;
                i++;
                afterSpace = false;
            }
            return Group(l);
        }
    }

    static class TexLinear
    {
        static readonly string SupDigits = "\u2070\u00B9\u00B2\u00B3\u2074\u2075\u2076\u2077\u2078\u2079";
        static readonly string SubDigits = "\u2080\u2081\u2082\u2083\u2084\u2085\u2086\u2087\u2088\u2089";

        public static string Of(MNode n)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, n, 0, false);
            return Tidy(sb.ToString());
        }

        public static string Tidy(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            bool sp = false;
            foreach (char c in s)
            {
                if (c == ' ') { sp = true; continue; }
                if (sp && sb.Length > 0 && sb[sb.Length - 1] != '\n' && c != '\n') sb.Append(' ');
                sp = false;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        public static void Atom(StringBuilder sb, MNode n, int cls, int prev, int next)
        {
            Atom(sb, n, cls, prev, next, 0, false);
        }

        static void Atom(StringBuilder sb, MNode n, int cls, int prev, int next, int depth, bool compact)
        {
            if (cls == Tex.Bin || cls == Tex.Rel) { sb.Append(' '); Write(sb, n, depth, compact); sb.Append(' '); return; }
            int at = sb.Length;
            Write(sb, n, depth, compact);
            if (cls == Tex.Punct) sb.Append(' ');
            else if ((cls == Tex.Op || (n.Kind == MNode.Frac && sb.Length - at > 1)) && (next == Tex.Ord || next == Tex.Inner || next == Tex.Op) && !compact) sb.Append(' ');
        }

        static void List(StringBuilder sb, List<MNode> list, int depth, bool compact)
        {
            if (list == null) return;
            int prev = -1;
            for (int i = 0; i < list.Count; i++)
            {
                MNode n = list[i];
                if (n.Kind == MNode.Space) { if (n.Size > 0.15) sb.Append(' '); continue; }
                if (n.Kind == MNode.Sty) continue;
                int cls = n.Cls;
                if (cls == Tex.Bin && (prev < 0 || prev == Tex.Bin || prev == Tex.Op || prev == Tex.Rel || prev == Tex.Open || prev == Tex.Punct)) cls = Tex.Ord;
                int next = -1;
                for (int j = i + 1; j < list.Count; j++) if (list[j].Kind != MNode.Space && list[j].Kind != MNode.Sty) { next = list[j].Cls; break; }
                if (cls == Tex.Bin && (next < 0 || next == Tex.Rel || next == Tex.Close || next == Tex.Punct)) cls = Tex.Ord;
                if (compact && (cls == Tex.Bin || cls == Tex.Rel) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    Write(sb, n, depth + 1, compact);
                    prev = cls;
                    continue;
                }
                Atom(sb, n, cls, prev, next, depth + 1, compact);
                prev = cls;
            }
        }

        static bool Simple(string t)
        {
            if (t.Length == 0) return true;
            if (t.Length == 1) return true;
            if (t.Length == 2 && char.IsSurrogatePair(t, 0)) return true;
            int brackets = 0;
            bool letters = true, digits = true;
            foreach (char c in t)
            {
                if (c == '(' || c == '[' || c == '{') brackets++;
                else if (c == ')' || c == ']' || c == '}') brackets--;
                if (!char.IsLetter(c)) letters = false;
                if (!char.IsDigit(c) && c != '.') digits = false;
            }
            if (digits || letters) return true;
            if (t.IndexOf(' ') >= 0) return false;
            foreach (char c in t) if ("+-\u2212=<>/\u00B1\u00D7\u22C5,".IndexOf(c) >= 0) return false;
            return true;
        }

        static string Arg(MNode n, int depth, bool compact)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, n, depth + 1, compact);
            return Tidy(sb.ToString());
        }

        static string Wrap(string t) { return Simple(t) ? t : "(" + t + ")"; }

        static string Map(string t, bool sup)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in t)
            {
                char m = '\0';
                if (c >= '0' && c <= '9') m = (sup ? SupDigits : SubDigits)[c - '0'];
                else
                    switch (c)
                    {
                        case '+': m = sup ? '\u207A' : '\u208A'; break;
                        case '-': case '\u2212': m = sup ? '\u207B' : '\u208B'; break;
                        case '=': m = sup ? '\u207C' : '\u208C'; break;
                        case '(': m = sup ? '\u207D' : '\u208D'; break;
                        case ')': m = sup ? '\u207E' : '\u208E'; break;
                        case 'n': m = sup ? '\u207F' : '\u2099'; break;
                        case 'i': m = sup ? '\u2071' : '\u1D62'; break;
                        case 'x': m = sup ? '\u02E3' : '\u2093'; break;
                        case 'T': if (sup) m = '\u1D40'; break;
                        case 'a': if (!sup) m = '\u2090'; break;
                        case 'e': if (!sup) m = '\u2091'; break;
                        case 'o': if (!sup) m = '\u2092'; break;
                        case 'k': if (!sup) m = '\u2096'; break;
                        case 'm': if (!sup) m = '\u2098'; break;
                        case 't': if (!sup) m = '\u209C'; break;
                        case 'j': if (!sup) m = '\u2C7C'; break;
                        case 'r': if (!sup) m = '\u1D63'; break;
                        case '\u2032': case '\u2033': if (sup) m = c; break;
                    }
                if (m == '\0') return null;
                sb.Append(m);
            }
            return sb.ToString();
        }

        static void Script(StringBuilder sb, string t, bool sup)
        {
            if (t.Length == 0) return;
            string m = t.Length <= 8 ? Map(t, sup) : null;
            if (m != null) { sb.Append(m); return; }
            sb.Append(sup ? '^' : '_');
            sb.Append(t.Length == 1 || (t.Length == 2 && char.IsSurrogatePair(t, 0)) ? t : "(" + t + ")");
        }

        static string Fraction(string a, string b)
        {
            switch (a + "/" + b)
            {
                case "1/2": return "\u00BD";
                case "1/3": return "\u2153";
                case "2/3": return "\u2154";
                case "1/4": return "\u00BC";
                case "3/4": return "\u00BE";
                case "1/5": return "\u2155";
                case "2/5": return "\u2156";
                case "3/5": return "\u2157";
                case "4/5": return "\u2158";
                case "1/6": return "\u2159";
                case "5/6": return "\u215A";
                case "1/8": return "\u215B";
                case "3/8": return "\u215C";
                case "5/8": return "\u215D";
                case "7/8": return "\u215E";
            }
            return null;
        }

        static bool Mark(char c)
        {
            UnicodeCategory k = CharUnicodeInfo.GetUnicodeCategory(c);
            return k == UnicodeCategory.NonSpacingMark || k == UnicodeCategory.EnclosingMark || k == UnicodeCategory.SpacingCombiningMark;
        }

        static string Combining(string accent)
        {
            switch (accent)
            {
                case "hat": case "widehat": return "\u0302";
                case "check": case "widecheck": return "\u030C";
                case "tilde": case "widetilde": return "\u0303";
                case "acute": return "\u0301";
                case "grave": return "\u0300";
                case "dot": return "\u0307";
                case "ddot": return "\u0308";
                case "dddot": return "\u20DB";
                case "breve": return "\u0306";
                case "bar": case "overline": return "\u0305";
                case "vec": case "overrightarrow": case "Overrightarrow": return "\u20D7";
                case "overleftarrow": return "\u20D6";
                case "overleftrightarrow": return "\u20E1";
                case "mathring": return "\u030A";
                case "underline": case "uline": return "\u0332";
                case "utilde": return "\u0330";
            }
            return null;
        }

        static void Write(StringBuilder sb, MNode n, int depth, bool compact)
        {
            if (n == null || depth > 60) return;
            switch (n.Kind)
            {
                case MNode.Sym:
                    sb.Append(n.Text);
                    break;
                case MNode.Txt:
                    sb.Append(n.Text);
                    break;
                case MNode.Group:
                    List(sb, n.List, depth, compact);
                    break;
                case MNode.Space:
                    if (n.Size > 0.15) sb.Append(' ');
                    break;
                case MNode.Script:
                    {
                        bool pre = n.A != null && n.A.Kind == MNode.Group && (n.A.List == null || n.A.List.Count == 0);
                        Write(sb, n.A, depth + 1, compact);
                        if (pre && n.B != null) Script(sb, Arg(n.B, depth, true), true);
                        if (n.C != null) Script(sb, Arg(n.C, depth, true), false);
                        if (!pre && n.B != null) Script(sb, Arg(n.B, depth, true), true);
                        if (n.A != null && n.A.Cls == Tex.Op && (n.A.Flags & MNode.OpName) == 0) sb.Append(' ');
                        break;
                    }
                case MNode.Frac:
                    {
                        string a = Arg(n.A, depth, compact), b = Arg(n.B, depth, compact);
                        if (n.Size == 0)
                        {
                            sb.Append("C(").Append(a).Append(", ").Append(b).Append(')');
                            break;
                        }
                        string v = Fraction(a, b);
                        if (v != null) { sb.Append(v); break; }
                        sb.Append(Wrap(a)).Append('/').Append(Wrap(b));
                        break;
                    }
                case MNode.Sqrt:
                    {
                        string idx = n.B != null ? Arg(n.B, depth, true) : "";
                        string body = Arg(n.A, depth, compact);
                        if (idx == "3") sb.Append('\u221B');
                        else if (idx == "4") sb.Append('\u221C');
                        else { if (idx.Length > 0) Script(sb, idx, true); sb.Append('\u221A'); }
                        sb.Append(Simple(body) ? body : "(" + body + ")");
                        break;
                    }
                case MNode.Fence:
                    if (n.L != ".") sb.Append(n.L);
                    sb.Append(Arg(n.A, depth, compact));
                    if (n.R != ".") sb.Append(n.R);
                    break;
                case MNode.Delim:
                    if (n.Text != ".") { if (n.Cls == Tex.Rel) sb.Append(' ').Append(n.Text).Append(' '); else sb.Append(n.Text); }
                    break;
                case MNode.Accent:
                    {
                        string b = Arg(n.A, depth, compact);
                        string comb = Combining(n.Text);
                        if (comb == null || b.Length == 0) { sb.Append(b); break; }
                        if ((n.Text == "overline" || n.Text == "underline" || n.Text == "uline") && b.Length <= 2000)
                        {
                            for (int i = 0; i < b.Length; i++)
                            {
                                char c = b[i];
                                sb.Append(c);
                                if (char.IsHighSurrogate(c) || char.IsWhiteSpace(c) || Mark(c)) continue;
                                while (i + 1 < b.Length && Mark(b[i + 1])) sb.Append(b[++i]);
                                sb.Append(comb);
                            }
                            break;
                        }
                        sb.Append(b).Append(comb);
                        break;
                    }
                case MNode.Array:
                    {
                        bool lines = n.Flags == 1 || n.Flags == 2 || n.Flags == 3;
                        if (n.L != null && n.L != ".") sb.Append(n.L);
                        for (int r = 0; r < n.Rows.Count; r++)
                        {
                            if (r > 0) sb.Append(lines ? "\n" : "; ");
                            for (int c = 0; c < n.Rows[r].Count; c++)
                            {
                                string cell = Arg(n.Rows[r][c], depth, compact);
                                if (c > 0) sb.Append(n.Flags == 2 ? (c % 2 == 1 ? " " : "    ") : n.Flags == 1 ? "  " : ", ");
                                sb.Append(cell);
                            }
                        }
                        if (n.R != null && n.R != ".") sb.Append(n.R);
                        break;
                    }
                case MNode.Boxed:
                case MNode.Cancel:
                    Write(sb, n.A, depth + 1, compact);
                    break;
                case MNode.Phantom:
                    if (n.Flags == 4) Write(sb, n.A, depth + 1, compact);
                    break;
                case MNode.Stack:
                    Write(sb, n.A, depth + 1, compact);
                    if (n.B != null) Script(sb, Arg(n.B, depth, true), true);
                    if (n.C != null) Script(sb, Arg(n.C, depth, true), false);
                    break;
                case MNode.XArrow:
                    {
                        string arrow = XArrowChar(n.Text);
                        string up = n.A != null ? Arg(n.A, depth, true) : "";
                        sb.Append(arrow);
                        if (up.Length > 0) sb.Append('[').Append(up).Append(']');
                        break;
                    }
            }
        }

        public static string XArrowChar(string name)
        {
            switch (name)
            {
                case "leftarrow": return "\u2190";
                case "Rightarrow": return "\u21D2";
                case "Leftarrow": return "\u21D0";
                case "leftrightarrow": return "\u2194";
                case "Leftrightarrow": return "\u21D4";
                case "mapsto": return "\u21A6";
                case "longequal": return "=";
                case "hookrightarrow": return "\u21AA";
                case "hookleftarrow": return "\u21A9";
                case "twoheadrightarrow": return "\u21A0";
                case "twoheadleftarrow": return "\u219E";
                case "rightharpoonup": return "\u21C0";
                case "rightharpoondown": return "\u21C1";
                case "leftharpoonup": return "\u21BC";
                case "leftharpoondown": return "\u21BD";
                case "rightleftharpoons": return "\u21CC";
                case "leftrightharpoons": return "\u21CB";
                case "tofrom": return "\u21C4";
            }
            return "\u2192";
        }
    }
}
