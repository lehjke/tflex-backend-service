using System;
using System.Globalization;

namespace TFlexAutomationRunner
{
    internal static class RealVariableValueFormatter
    {
        public static string Format(object value)
        {
            if (value == null)
            {
                return "0";
            }

            if (value is bool)
            {
                return (bool)value ? "1" : "0";
            }

            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
                default:
                    throw new InvalidOperationException(
                        "A real T-FLEX variable can receive only a numeric or boolean value.");
            }
        }
    }
}
