using System.Globalization;

namespace MapleBar;

/// <summary>계산기 스크립트 하나: 이름, 식, 변수마다 마지막에 넣은 값 (설정에 저장한다)</summary>
public class CalcScript
{
    public string Title { get; set; } = "";
    public string Expr { get; set; } = "";
    /// <summary>변수 이름(소문자) → 마지막에 넣은 글자. 식에서 잠깐 빠진 변수 값도 남겨 둔다</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}

/// <summary>식이 잘못 쓰였을 때 (결과 자리에 Message를 그대로 보여 준다)</summary>
sealed class ScriptError(string message) : Exception(message);

/// <summary>
/// 글로 쓴 식을 계산한다: + - * / ^ 괄호, 소수, 세 자리 쉼표, 변수.
/// 변수 이름은 글자(영문·한글)나 _로 시작하고 글자·숫자·_로 이어진다. 대소문자는 가리지 않는다.
/// 숫자 바로 뒤의 만·억·조는 단위로 읽고("1억 2,000만"), 2x·2(x+1)처럼 붙여 쓰면 곱한다.
/// </summary>
static class ScriptMath
{
    public static string Key(string name) => name.ToLowerInvariant();

    static bool NameStart(char c) => char.IsLetter(c) || c == '_';
    static bool NameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>s[i]가 앞 숫자에 붙은 만·억·조이면 그 크기, 아니면 0</summary>
    static decimal UnitAt(string s, int i)
    {
        if (i >= s.Length || (i + 1 < s.Length && NameChar(s[i + 1]))) return 0;
        return s[i] switch { '만' => 1_0000m, '억' => 1_0000_0000m, '조' => 1_0000_0000_0000m, _ => 0 };
    }

    /// <summary>숫자 한 덩어리를 건너뛴 끝 자리 (쉼표는 뒤에 숫자가 올 때만 숫자의 일부)</summary>
    static int SkipNumber(string s, int i)
    {
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || (s[i] == ',' && i + 1 < s.Length && char.IsDigit(s[i + 1])))) i++;
        return i;
    }

    /// <summary>식에 나온 변수 이름들 (처음 나온 차례, 처음 쓴 모양 그대로, 겹치지 않게)</summary>
    public static List<string> Names(string expr)
    {
        var list = new List<string>();
        var seen = new HashSet<string>();
        for (int i = 0; i < expr.Length;)
        {
            char c = expr[i];
            if (char.IsDigit(c) || c == '.')
            {
                i = SkipNumber(expr, i);
                if (UnitAt(expr, i) > 0) i++;
                continue;
            }
            if (NameStart(c))
            {
                int b = i;
                while (i < expr.Length && NameChar(expr[i])) i++;
                string n = expr[b..i];
                if (seen.Add(Key(n))) list.Add(n);
                continue;
            }
            i++;
        }
        return list;
    }

    /// <summary>
    /// 식을 계산한다. 식이 틀리면 ScriptError, 0으로 나누면 DivideByZeroException, 너무 크면 OverflowException.
    /// get은 변수 이름을 받아 값을 돌려준다
    /// </summary>
    public static decimal Eval(string expr, Func<string, decimal> get)
    {
        if (expr.Trim().Length == 0) throw new ScriptError("식을 써 주세요");
        var p = new Parser(expr, get);
        decimal v = p.Sum();
        if (p.Peek() == ')') throw new ScriptError("괄호가 맞지 않아요");
        if (p.Peek() != '\0') throw new ScriptError("식이 틀렸어요");
        return v;
    }

    /// <summary>식의 모양만 본다 (값은 모두 1로 두고 계산). 틀렸으면 알릴 말, 괜찮으면 null</summary>
    public static string? Check(string expr)
    {
        try { Eval(expr, _ => 1); return null; }
        catch (ScriptError e) { return e.Message; }
        catch (Exception) { return null; } // 0으로 나누기·큰 수는 값에 따라 다르니 여기서 보지 않는다
    }

    sealed class Parser(string s, Func<string, decimal> get)
    {
        int i;
        // 괄호·부호·거듭제곱이 겹칠 때마다 Signed로 한 단계 깊어진다. 너무 깊으면 앱이 꺼지므로 그 전에 멈춘다
        const int MaxDepth = 200;
        int depth;

        public char Peek()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i < s.Length ? s[i] : '\0';
        }

        // 덧셈·뺄셈
        public decimal Sum()
        {
            decimal v = Product();
            while (true)
            {
                char c = Peek();
                if (c == '+') { i++; v += Product(); }
                else if (c is '-' or '−') { i++; v -= Product(); }
                else return v;
            }
        }

        // 곱셈·나눗셈 (붙여 쓴 곱셈 포함)
        decimal Product()
        {
            decimal v = Signed();
            while (true)
            {
                char c = Peek();
                if (c is '*' or '×' or '·') { i++; v *= Signed(); }
                else if (c is '/' or '÷') { i++; v /= Signed(); }
                else if (c == '(' || NameStart(c)) v *= Signed();
                else return v;
            }
        }

        // 앞에 붙은 + -
        decimal Signed()
        {
            if (++depth > MaxDepth) throw new ScriptError("계산할 수 없는 식이에요");
            try
            {
                char c = Peek();
                if (c is '-' or '−') { i++; return -Signed(); }
                if (c == '+') { i++; return Signed(); }
                return Power();
            }
            finally { depth--; }
        }

        // 거듭제곱 (^ 또는 **, 오른쪽부터)
        decimal Power()
        {
            decimal b = Atom();
            char c = Peek();
            if (c == '^') { i++; return Pow(b, Signed()); }
            if (c == '*' && i + 1 < s.Length && s[i + 1] == '*') { i += 2; return Pow(b, Signed()); }
            return b;
        }

        decimal Atom()
        {
            char c = Peek();
            if (c == '(')
            {
                i++;
                decimal v = Sum();
                if (Peek() != ')') throw new ScriptError("괄호가 맞지 않아요");
                i++;
                return v;
            }
            if (char.IsDigit(c) || c == '.') return Number();
            if (NameStart(c))
            {
                int b = i;
                while (i < s.Length && NameChar(s[i])) i++;
                return get(s[b..i]);
            }
            if (c == '\0') throw new ScriptError("식이 끝나지 않았어요");
            if (c == ')') throw new ScriptError("괄호가 맞지 않아요");
            throw new ScriptError($"'{c}'는 쓸 수 없어요");
        }

        /// <summary>수 하나. "1억 2,000만 500"처럼 큰 단위부터 이어 쓴 수는 더한다</summary>
        decimal Number()
        {
            decimal total = OnePiece(out decimal unit);
            while (unit > 1)
            {
                int save = i;
                char c = Peek();
                if (!char.IsDigit(c)) { i = save; break; }
                int keep = i;
                decimal v = OnePiece(out decimal u);
                if (u >= unit) { i = keep; break; } // 큰 단위가 뒤에 오면 이어 쓴 수가 아니다
                total += v;
                unit = u;
            }
            return total;
        }

        decimal OnePiece(out decimal unit)
        {
            int b = i;
            i = SkipNumber(s, i);
            string t = s[b..i].Replace(",", "");
            if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal v))
            {
                if (t.Count(ch => ch == '.') > 1 || t == ".") throw new ScriptError("숫자가 틀렸어요");
                throw new OverflowException();
            }
            unit = UnitAt(s, i);
            if (unit > 0) { i++; v *= unit; } else unit = 1;
            return v;
        }
    }

    static decimal Pow(decimal b, decimal e)
    {
        if (e == Math.Truncate(e) && Math.Abs(e) <= 10000)
        {
            long n = (long)Math.Abs(e);
            decimal r = 1, x = b;
            while (n > 0)
            {
                if ((n & 1) == 1) r *= x;
                n >>= 1;
                if (n > 0) x *= x;
            }
            return e < 0 ? 1 / r : r;
        }
        double d = Math.Pow((double)b, (double)e);
        if (double.IsNaN(d)) throw new ScriptError("계산할 수 없는 식이에요");
        if (double.IsInfinity(d) || Math.Abs(d) > (double)decimal.MaxValue) throw new OverflowException();
        return (decimal)d;
    }
}
