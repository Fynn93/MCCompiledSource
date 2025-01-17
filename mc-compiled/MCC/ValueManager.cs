﻿using mc_compiled.Commands;
using mc_compiled.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mc_compiled.MCC
{
    /// <summary>
    /// Caches and tracks values and their types. Also holds onto and enforces struct definitions.
    /// In a sense, this class doubles as an advanced code generator too.
    /// </summary>
    public class ValueManager
    {
        private readonly StructDefinition[] BUILT_IN_STRUCTS =
        {
            new StructDefinition("point", "x", "y"),
            new StructDefinition("color", "r", "g", "b")
        };

        List<StructDefinition> structs;
        Dictionary<string, Value> values;
        public int Count
        {
            get; private set;
        }

        public ValueManager()
        {
            Count = 0;
            values = new Dictionary<string, Value>();

            structs = new List<StructDefinition>();
            structs.AddRange(BUILT_IN_STRUCTS);
        }
        /// <summary>
        /// Get a structure definition for a specific value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public StructDefinition GetStructForValue(Value value)
        {
            if (value.type != ValueType.STRUCT)
                throw new ArgumentException("Value must be a struct instance.");
            return structs[value.information];
        }

        public string[] DefineValue(string expression)
        {
            string[] args = expression.Split(' ');

            if (args.Length < 1)
                throw new ArgumentException("No arguments specified to define expression.");

            if(args.Length == 1)
            {
                string valueName = args[0];
                return DefineValue(valueName, ValueType.REGULAR, 0);
            } else
            {
                string valueName = args[args.Length - 1];
                string _type = args[0];

                if(structs.Any(sd => sd.name.Equals(_type)))
                {
                    int index = structs.IndexOf(structs.First(sd => sd.name.Equals(_type)));
                    return DefineValue(valueName, ValueType.STRUCT, index);
                } else if(_type.ToUpper().Equals("DECIMAL"))
                {
                    if (!int.TryParse(args[1], out int precision))
                        throw new ArgumentException("No valid precision specified for decimal value.");
                    return DefineValue(valueName, ValueType.DECIMAL, precision);
                } else
                {
                    return DefineValue(valueName, ValueType.REGULAR, 0);
                }
            }
        }
        /// <summary>
        /// Define a value interally.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="type"></param>
        /// <param name="information"></param>
        /// <returns>The commands to setup this value.</returns>
        public string[] DefineValue(string name, ValueType type = ValueType.REGULAR, int information = 0)
        {
            if(name.Any(c => !CommandLimits.SCOREBOARD_ALLOWED.Contains(c)))
                throw new ArgumentException($"Illegal character in value name \"{name}\"");
            if (name.Length > CommandLimits.SCOREBOARD_LIMIT)
                throw new ArgumentException($"Value name cannot be longer than {CommandLimits.SCOREBOARD_LIMIT} characters.");
            // Reserve 2 characters for the compressed struct parameters.
            if(type != ValueType.REGULAR && name.Length > CommandLimits.SCOREBOARD_LIMIT - 2)
                throw new ArgumentException($"Typed value name cannot be longer than {CommandLimits.SCOREBOARD_LIMIT - 2} characters.");

            Value value = new Value(name, information, type);
            values.Add(name, value);
            Count++;

            string[] scoreboards = value.GetScoreboards(this);
            return (from s in scoreboards select $"scoreboard objectives add \"{s}\" dummy").ToArray();
        }

        public bool HasValue(string name)
        {
            int index;
            if ((index = name.IndexOf(':')) != -1)
            {
                if (name.EndsWith(":") || name.StartsWith(":"))
                    throw new ArgumentException("Value name cannot start or end with an accessor (: or .)");
                string main = name.Substring(0, index);
                string sub = name.Substring(index + 1);
                if (values.TryGetValue(main, out Value output))
                {
                    StructDefinition info = GetStructForValue(output);
                    return info.fields.Contains(sub);
                }
                else return false;
            }
            else
                return values.ContainsKey(name);
        }
        public bool TryGetValue(string name, out Value output)
        {
            int index;
            if((index = name.IndexOf(':')) != -1)
            {
                if (name.EndsWith(":") || name.StartsWith(":"))
                    throw new ArgumentException("Value name cannot start or end with an accessor (: or .)");
                string main = name.Substring(0, index);
                string sub = name.Substring(index + 1);
                if (values.TryGetValue(main, out output))
                {
                    StructDefinition info = GetStructForValue(output);
                    return info.fields.Contains(sub);
                }
                else return false;
            } else
                return values.TryGetValue(name, out output);
        }
        public Value GetValue(string name)
        {
            int index;
            if ((index = name.IndexOf(':')) != -1)
            {
                if (name.EndsWith(":") || name.StartsWith(":"))
                    throw new ArgumentException("Value name cannot start or end with an accessor (: or .)");
                string main = name.Substring(0, index);
                return values[main];
            }
            else
                return values[name];
        }
        public Value this[string valueName]
        {
            get { return values[valueName]; }
            private set { values[valueName] = value; }
        }

        public static string[] ExpressionAddConstant(Value source, string selector, Dynamic value)
        {
            if (source.type == ValueType.REGULAR)
                return new[] { $"scoreboard players add {selector} {source} {value.data.i}" };
            else if (source.type == ValueType.DECIMAL)
            {
                int p = source.information;
                string unit = ((int)Math.Pow(10, p)).ToString(); // 10^unit; 100:2 1000:3 etc...
                return new[]
                {
                    // Set decimal unit
$"scoreboard players set {selector} {Executor.DECIMAL_UNIT} {unit}",

                    // Add whole/decimal part
$"scoreboard players add {selector} {source.WholePart} {value.GetWholePart()}",
$"scoreboard players add {selector} {source.DecimalPart} {value.GetDecimalPart(p)}",

                    // Carry leftover part, if any.
$"scoreboard players operation {selector} {Executor.MATH_TEMP} = {selector} {source.DecimalPart}",      // temp = value.d
$"scoreboard players operation {selector} {Executor.MATH_TEMP} /= {selector} {Executor.DECIMAL_UNIT}",  // temp /= unit
$"scoreboard players operation {selector} {source.WholePart} += {selector} {Executor.MATH_TEMP}",       // value.w += temp
$"scoreboard players operation {selector} {Executor.MATH_TEMP} *= {selector} {Executor.DECIMAL_UNIT}",  // temp *= unit
$"scoreboard players operation {selector} {source.DecimalPart} -= {selector} {Executor.MATH_TEMP}"      // value.d -= temp
                };
            }
            else return null;
        }
        public static string[] ExpressionSubtractConstant(Value source, string selector, Dynamic value)
        {
            if (source.type == ValueType.REGULAR)
                return new[] { $"scoreboard players add {selector} {source} {value.data.i * -1}" };
            else if (source.type == ValueType.DECIMAL)
            {
                if (value.data.d < 0.0)
                    return ExpressionAddConstant(source, selector, value.Inverse());

                int p = source.information;
                string unit = ((int)Math.Pow(10, p)).ToString(); // 10^unit; 100:2 1000:3 etc...
                string functionName = Executor.DECIMAL_SUB_CARRY + source.name;

                string functionCallLine;
                if (selector.StartsWith("@p"))
                    functionCallLine = $"execute {selector} ~~~ execute @s[scores={{{source.DecimalPart}=..0}}] ~~~ function {functionName}";
                else
                    functionCallLine = $"execute {selector}[scores={{{source.DecimalPart}=..0}}] ~~~ function {functionName}";

                return new[]
                {
                    // Set decimal unit
$"scoreboard players set {selector} {Executor.DECIMAL_UNIT} {unit}",

                    // Add whole/decimal part
$"scoreboard players add {selector} {source.WholePart} {value.GetWholePart()}",
$"scoreboard players add {selector} {source.DecimalPart} -{value.GetDecimalPart(p)}",

                    // Carry leftover part, if any.
                    functionCallLine
                };
            }
            else return null;
        }
        public static string[] ExpressionMultiplyConstant(Value source, string selector, Dynamic value)
        {
            if (source.type == ValueType.REGULAR)
                return new[]
                {
$"scoreboard players set {selector} {Executor.MATH_TEMP} {value.data.i}",
$"scoreboard players operation {selector} {source} *= {selector} {Executor.MATH_TEMP}"
                };
            else if (source.type == ValueType.DECIMAL)
            {
                int p = source.information;
                string unit = ((int)Math.Pow(10, p)).ToString(); // 10^unit; 100:2 1000:3 etc...
                return new[]
                {
                    // Set decimal unit
$"scoreboard players set {selector} {Executor.DECIMAL_UNIT} {unit}",

                    // Add whole/decimal part
$"scoreboard players set {selector} {Executor.MATH_TEMP} {value.GetWholePart()}",
$"scoreboard players operation {selector} {source.WholePart} *= {selector} {Executor.MATH_TEMP}",
$"scoreboard players set {selector} {Executor.MATH_TEMP} {value.GetDecimalPart()}",
$"scoreboard players operation {selector} {source.DecimalPart} *= {selector} {Executor.MATH_TEMP}",

                    // Carry leftover part, if any.
$"scoreboard players operation {selector} {Executor.MATH_TEMP} = {selector} {source.DecimalPart}",      // temp = value.d
$"scoreboard players operation {selector} {Executor.MATH_TEMP} /= {selector} {Executor.DECIMAL_UNIT}",  // temp /= unit
$"scoreboard players operation {selector} {source.WholePart} += {selector} {Executor.MATH_TEMP}",       // value.w += temp
$"scoreboard players operation {selector} {Executor.MATH_TEMP} *= {selector} {Executor.DECIMAL_UNIT}",  // temp *= unit
$"scoreboard players operation {selector} {source.DecimalPart} -= {selector} {Executor.MATH_TEMP}"      // value.d -= temp
                };
            }
            else return null;
        }
        public static string[] ExpressionDivideConstant(Value source, string selector, Dynamic value)
        {
            if (source.type == ValueType.REGULAR)
            {
                if (value.data.i == 0)
                    throw new Exception("Cannot divide by zero.");
                return new[]
                {
$"scoreboard players set {selector} {Executor.MATH_TEMP} {value.data.i}",
$"scoreboard players operation {selector} {source} /= {selector} {Executor.MATH_TEMP}"
                };
            }
            else if (source.type == ValueType.DECIMAL)
            {
                throw new Exception("Decimal over decimal division is not supported.");
            }
            else return null;
        }
        public static string[] ExpressionModuloConstant(Value source, string selector, Dynamic value)
        {
            if (source.type == ValueType.REGULAR)
            {
                if (value.data.i == 0)
                    throw new Exception("Cannot divide by zero.");
                return new[]
                {
$"scoreboard players set {selector} {Executor.MATH_TEMP} {value.data.i}",
$"scoreboard players operation {selector} {source} %= {selector} {Executor.MATH_TEMP}"
                };
            }
            else if (source.type == ValueType.DECIMAL)
            {
                throw new Exception("Decimal over decimal modulus is not supported.");
            }
            else return null;
        }
        public static string[] ExpressionSetConstant(Value source, string selector, Dynamic value)
        {
            if (source.type == ValueType.REGULAR)
            {
                return new[]
                {
$"scoreboard players set {selector} {source} {value.data.i}",
                };
            }
            else if (source.type == ValueType.DECIMAL)
            {
                int precision = source.information;
                int mult = (int)Math.Pow(10, precision);
                long wholePart = value.GetWholePart();
                long decimalPart = (long)Math.Round(value.GetDecimalPart() * mult);
                return new[]
                {
$"scoreboard players set {selector} {source.WholePart} {wholePart}",
$"scoreboard players set {selector} {source.DecimalPart} {decimalPart}",
                };
            }
            else return null;
        }

        public static string[] ExpressionAddValue(Value source, Value b, string selector)
        {
            if (source.type == ValueType.REGULAR)
                return new[] { $"scoreboard players operation {selector} {source} += {selector} {(b.type == ValueType.DECIMAL ? b.WholePart : b.ToString())}" };
            else if (source.type == ValueType.DECIMAL)
            {
                int p = source.information;
                string unit = ((int)Math.Pow(10, p)).ToString(); // 10^unit; 100:2 1000:3 etc...
                return new[]
                {
                    // Set decimal unit
$"scoreboard players set {selector} {Executor.DECIMAL_UNIT} {unit}",

                    // Add whole/decimal part
$"scoreboard players operation {selector} {source.WholePart} += {selector} {b.WholePart}",
$"scoreboard players operation {selector} {source.DecimalPart} += {selector} {b.DecimalPart}",

                    // Carry leftover part, if any.
$"scoreboard players operation {selector} {Executor.MATH_TEMP} = {selector} {source.DecimalPart}",      // temp = value.d
$"scoreboard players operation {selector} {Executor.MATH_TEMP} /= {selector} {Executor.DECIMAL_UNIT}",  // temp /= unit
$"scoreboard players operation {selector} {source.WholePart} += {selector} {Executor.MATH_TEMP}",       // value.w += temp
$"scoreboard players operation {selector} {Executor.MATH_TEMP} *= {selector} {Executor.DECIMAL_UNIT}",  // temp *= unit
$"scoreboard players operation {selector} {source.DecimalPart} -= {selector} {Executor.MATH_TEMP}"      // value.d -= temp
                };
            }
            else return null;
        }
        public static string[] ExpressionSubtractValue(Value source, Value b, string selector)
        {
            if (source.type == ValueType.REGULAR)
                return new[] { $"scoreboard players operation {selector} {source} -= {selector} {(b.type == ValueType.DECIMAL ? b.WholePart : b.ToString())}" };
            else if (source.type == ValueType.DECIMAL)
            {
                int p = source.information;
                string unit = ((int)Math.Pow(10, p)).ToString(); // 10^unit; 100:2 1000:3 etc...
                string functionName = Executor.DECIMAL_SUB_CARRY + source.name;

                string functionCallLine;
                if (selector.StartsWith("@p"))
                    functionCallLine = $"execute {selector} ~~~ execute @s[scores={{{source.DecimalPart}=..0}}] ~~~ function {functionName}";
                else
                    functionCallLine = $"execute {selector}[scores={{{source.DecimalPart}=..0}}] ~~~ function {functionName}";

                return new[]
                {
                    // Set decimal unit
$"scoreboard players set {selector} {Executor.DECIMAL_UNIT} {unit}",

                    // Add whole/decimal part
$"scoreboard players operation {selector} {source.WholePart} -= {selector} {b.WholePart}",
$"scoreboard players operation {selector} {source.DecimalPart} -= {selector} {b.DecimalPart}",

                    // Carry leftover part, if any.
                    functionCallLine
                };
            }
            else return null;
        }
        public static string[] ExpressionMultiplyValue(Value source, Value b, string selector)
        {
            if (source.type == ValueType.REGULAR)
                return new[]
                {
$"scoreboard players operation {selector} {source} *= {selector} {(b.type==ValueType.DECIMAL?b.WholePart:b.ToString())}"
                };
            else if (source.type == ValueType.DECIMAL)
            {
                int p = source.information;
                string unit = ((int)Math.Pow(10, p)).ToString(); // 10^unit; 100:2 1000:3 etc...
                return new[]
                {
                    // Set decimal unit
$"scoreboard players set {selector} {Executor.DECIMAL_UNIT} {unit}",

                    // Add whole/decimal part
$"scoreboard players operation {selector} {source.WholePart} *= {selector} {b.WholePart}",
$"scoreboard players operation {selector} {source.DecimalPart} *= {selector} {b.DecimalPart}",

                    // Carry leftover part, if any.
$"scoreboard players operation {selector} {Executor.MATH_TEMP} = {selector} {source.DecimalPart}",      // temp = value.d
$"scoreboard players operation {selector} {Executor.MATH_TEMP} /= {selector} {Executor.DECIMAL_UNIT}",  // temp /= unit
$"scoreboard players operation {selector} {source.WholePart} += {selector} {Executor.MATH_TEMP}",       // value.w += temp
$"scoreboard players operation {selector} {Executor.MATH_TEMP} *= {selector} {Executor.DECIMAL_UNIT}",  // temp *= unit
$"scoreboard players operation {selector} {source.DecimalPart} -= {selector} {Executor.MATH_TEMP}"      // value.d -= temp
                };
            }
            else return null;
        }
        public static string[] ExpressionDivideValue(Value source, Value b, string selector)
        {
            if (source.type == ValueType.REGULAR)
            {
                return new[]
                {
$"scoreboard players operation {selector} {source} /= {selector} {(b.type==ValueType.DECIMAL?b.WholePart:b.ToString())}"
                };
            }
            else if (source.type == ValueType.DECIMAL || b.type == ValueType.DECIMAL)
            {
                throw new Exception("Decimal over decimal division is not supported.");
            }
            else return null;
        }
        public static string[] ExpressionModuloValue(Value source, Value b, string selector)
        {
            if (source.type == ValueType.REGULAR)
            {
                return new[]
                {
$"scoreboard players operation {selector} {source} %= {selector} {(b.type==ValueType.DECIMAL?b.WholePart:b.ToString())}"
                };
            }
            else if (source.type == ValueType.DECIMAL || b.type == ValueType.DECIMAL)
            {
                throw new Exception("Decimal over decimal modulus is not supported.");
            }
            else return null;
        }
        public static string[] ExpressionSetValue(Value source, Value b, string selector)
        {
            if (source.type == ValueType.REGULAR)
            {
                return new[]
                {
$"scoreboard players operation {selector} {source} = {selector} {b}",
                };
            }
            else if (source.type == ValueType.DECIMAL)
            {
                if (b.type == ValueType.DECIMAL)
                {
                    return new[] {
$"scoreboard players operation {selector} {source.WholePart} = {selector} {b.WholePart}",
$"scoreboard players operation {selector} {source.DecimalPart} = {selector} {b.DecimalPart}",
                    };
                }
                else if (b.type == ValueType.REGULAR)
                {
                    return new[] {
$"scoreboard players operation {selector} {source.WholePart} = {selector} {b}",
$"scoreboard players set {selector} {source.DecimalPart} 0",
                    };
                }
                else throw new NotImplementedException("Assigning decimal value from struct field is not supported yet.");
            }
            else return null;
        }
    }
    /// <summary>
    /// An operation on two values. If (value % 2 == 1), then a temp scoreboard objective is needed.
    /// </summary>
    enum ValueOperation : byte
    {
        ADD = 0,
        SUB = 2,
        MUL = 3,
        SET = 4,
        DIV = 5,
        MOD = 7,
    }
    public enum ValueType
    {
        REGULAR,
        DECIMAL,
        STRUCT, // Built-in types use this endpoint too.
    }
    public struct Value
    {
        public string name;
        public int information;
        public ValueType type;

        public Value(string name, int information = 0, ValueType type = ValueType.REGULAR)
        {
            this.name = name;
            this.information = information;
            this.type = type;
        }
        public Value(string name, Dynamic copyFrom)
        {
            if (copyFrom.type == Dynamic.Type.STRING)
            {
                this.name = copyFrom.data.s;
                information = 0;
                type = ValueType.REGULAR;
            }
            else if (copyFrom.type == Dynamic.Type.DECIMAL)
            {
                this.name = name;
                information = copyFrom.data.d.GetPrecision();
                type = ValueType.DECIMAL;
            }
            else
            {
                this.name = name;
                information = 0;
                type = ValueType.REGULAR;
            }
        }
        /// <summary>
        /// Get all the scoreboard entry names that this value uses.
        /// </summary>
        /// <param name="manager"></param>
        /// <returns></returns>
        public string[] GetScoreboards(ValueManager manager)
        {
            switch (type)
            {
                case ValueType.REGULAR:
                    return new string[] { name };
                case ValueType.DECIMAL:
                    return new string[] { $"{name}:w", $"{name}:d" };
                case ValueType.STRUCT:
                    StructDefinition info = manager.GetStructForValue(this);
                    string[] result = new string[info.internalFields.Length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = $"{name}:{info.internalFields[i]}";
                    return result;
                default:
                    return null;
            }
        }
        /// <summary>
        /// Converts this value to its representation as rawtext.
        /// </summary>
        /// <param name="selector"></param>
        /// <returns></returns>
        public JSONRawTerm[] ToRawText(ValueManager manager, string selector)
        {
            switch (type)
            {
                case ValueType.REGULAR:
                    return new JSONRawTerm[] { new JSONScore(selector, name) };
                case ValueType.DECIMAL:
                    return new JSONRawTerm[]
                    {
                        new JSONScore(selector, WholePart),
                        new JSONText("."),
                        new JSONScore(selector, DecimalPart)
                    };
                case ValueType.STRUCT:
                    StructDefinition info = manager.GetStructForValue(this);
                    JSONRawTerm[] ret = new JSONRawTerm[(info.fields.Length * 3 - 1) + 2];
                    ret[0] = new JSONText("(");
                    ret[ret.Length - 1] = new JSONText(")");
                    int write = 1;
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[write++] = new JSONText(info.fields[i] + ": ");
                        ret[write++] = new JSONScore(selector, name + ":" + info.internalFields[i]);
                        if (i + 1 != ret.Length)
                            ret[write++] = new JSONText(", ");
                    }
                    return ret;
                default:
                    return new JSONRawTerm[] { new JSONScore(selector, name) };
            }
        }
        public override string ToString()
        {
            return name;
        }

        public string WholePart
        {
            get { return name + ":w"; }
        }
        public string DecimalPart
        {
            get { return name + ":d"; }
        }
    }
    public struct StructDefinition
    {
        private static readonly char[] TRANSLATION_KEYS = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_".ToCharArray();
        private static readonly int MAX_FIELDS = TRANSLATION_KEYS.Length;

        public string name;
        public string[] fields;
        public char[] internalFields;

        public StructDefinition(string name, params string[] fields)
        {
            this.name = name;
            fields = fields.Distinct().ToArray();

            if (fields.Any(f => f.Any(c => !TRANSLATION_KEYS.Contains(c))))
                throw new ArgumentException("One of the fields in this struct has a non-standard character.");

            int len = fields.Length;
            if (len > MAX_FIELDS)
                throw new ArgumentException("Too many fields in user-defined struct. Max is " + MAX_FIELDS);

            this.fields = fields;

            internalFields = new char[len];
            for(int i = 0; i < len; i++)
            {
                string current = fields[i];
                if (current.Length == 1)
                    internalFields[i] = current[0];
                else internalFields[i] = TRANSLATION_KEYS[i];
            }
        }
    }
}