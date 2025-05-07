using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.Aliyun.Convert
{
    public static class ConvertExtensions
    {
        /// <summary>
        /// 时间转字串格式
        /// </summary>
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// 根据日期获取时间戳 13为（Java时间戳）
        /// </summary>
        /// <param name="date">时间</param>
        /// <param name="unit">单位，0=秒（s），1=毫秒（ms）</param>
        /// <returns></returns>
        public static long ToTimestamp(this DateTime date, int unit = 0)
        {
            return unit switch
            {
                0 => (date.ToUniversalTime().Ticks - 621355968000000000) / 10000000,
                1 => (date.ToUniversalTime().Ticks - 621355968000000000) / 10000,
                _ => 0,
            };
        }

        public static Dictionary<string, object> ToDictionary(this object obj)
        {
            Dictionary<string, object> data = [];
            Type type = obj.GetType();
            foreach (PropertyInfo fieldinfo in type.GetProperties())
            {
                object value = fieldinfo.GetValue(obj, null);
                if (fieldinfo.Name != null && value != null)
                {
                    data[fieldinfo.Name] = value;
                }
            }

            return data;
        }

        /// <summary>
        /// 把序列分成多批
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="source">数据集</param>
        /// <param name="batchSize">分批大小</param>
        /// <returns>多批数据</returns>
        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> source, int batchSize)
        {
            var foo = source.Select((value, index) => new { Index = index, Value = value, GroupIndex = index / batchSize }).GroupBy(x => x.GroupIndex).Select(x => x.Select(v => v.Value));
            // var foo = ts.Select((value, index) => new { Index = index, Value = value, GroupIndex = index / batchSize }).GroupBy(x => x.GroupIndex, (_, x) => x.Select(v => v.Value));

            return foo;
        }

        public static T GetValue<T>(this Dictionary<string, object> dict, string key, T defaultValue)
        {
            if (string.IsNullOrEmpty(key))
            {
                return default;
            }

            if (dict.TryGetValue(key, out object value))
            {
                if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?))
                {
                    DateTime.TryParse(value.ToString(), out DateTime time);
                    return (T)System.Convert.ChangeType(time, typeof(DateTime));
                }
                return (T)(value ?? default(T));
            }

            return defaultValue;
        }

        public static string GetAbsoluteUri(this HttpRequest request)
        {
            return new StringBuilder()
             .Append(request.Scheme)
             .Append("://")
             .Append(request.Host)
             .Append(request.PathBase)
             .Append(request.Path)
             .Append(request.QueryString)
             .ToString();
        }

        public static T ToEntity<T>(this Dictionary<string, object> obj)
        {
            if (obj == null)
            {
                return default;
            }

            T model = Activator.CreateInstance<T>();
            foreach (PropertyInfo property in model.GetType().GetProperties())
            {
                try
                {
                    string propertyName = property.Name;
                    if (!obj.Keys.Contains(propertyName) || obj[propertyName] == null)
                    {
                        continue;
                    }

                    object value = obj[propertyName];
                    Type valueType = value.GetType();
                    Type propertyType = property.PropertyType;
                    // 只考虑简单类型
                    // 日期转时间
                    // 枚举类型
                    if (valueType == propertyType || (value is int && propertyType.Name.Equals("boolean", StringComparison.OrdinalIgnoreCase)))
                    {
                        property.SetValue(model, System.Convert.ChangeType(value, propertyType), null);
                    }
                    else if (valueType.Name.Equals("DateTime", StringComparison.OrdinalIgnoreCase)
                        && propertyType.Name.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        property.SetValue(model, System.Convert.ToDateTime(value).ToString(DateTimeFormat), null);
                    }
                    else if (propertyType.IsEnum && (value is int || value is byte))
                    {
                        property.SetValue(model, Enum.ToObject(propertyType, value), null);
                    }
                    else if ((propertyType.IsClass || typeof(IEnumerable).IsAssignableFrom(propertyType) || propertyType.IsArray) && valueType.Name.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        if (value != null)
                        {
                            property.SetValue(model, JsonConvert.DeserializeObject(value.ToString(), propertyType), null);
                        }
                    }
                    else if (value is DataTable)
                    {
                        property.SetValue(model, DataTableToList(propertyType, (DataTable)value), null);
                    }
                    else
                    {
                        bool isCanConvert = false;
                        object objConvert = ConvertType(value, property, model, out isCanConvert);
                        property.SetValue(model, isCanConvert ? objConvert : System.Convert.ChangeType(value, propertyType), null);
                    }
                }
                catch
                {
                    return default(T);
                }
            }

            return model;
        }

        private static string ConvertValueToString(object val)
        {
            if (val is bool)
            {
                return (val.ToString().ToLower() == "true") ? "1" : "0";
            }

            return val.ToString();
        }

        /// <summary>
        /// 通过反射TryParse方法进行类型转换，如果类型没有TryParse方法，则返回null,并且parseSuccess==false
        /// </summary>
        /// <param name="val">需要转换的值</param>
        /// <param name="propertyInfo">属性</param>
        /// <param name="obj">泛型类型实例</param>
        /// <param name="parseSuccess"> 只有当前类型不存在TryParse方法的时候才会返回false</param>
        /// <returns>object实例</returns>
        private static object ConvertType<T>(object val, PropertyInfo propertyInfo, T obj, out bool parseSuccess)
        {
            parseSuccess = true;
            Type tp = propertyInfo.PropertyType;
            // 类型的默认值
            if (val == null)
            {
                return null;
            }

            // 泛型Nullable判断，取其中的类型
            if (tp.IsGenericType)
            {
                tp = tp.GetGenericArguments()[0];
            }

            // string直接返回转换
            if (tp.Name.ToLower() == "string")
            {
                return val.ToString();
            }

            // 反射获取TryParse方法
            MethodInfo tryParse = tp.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static, Type.DefaultBinder, new Type[] { typeof(string), tp.MakeByRefType() }, new ParameterModifier[] { new ParameterModifier(2) });
            // 只有当前类型不存在TryParse方法的时候才会返回false
            if (tryParse == null)
            {
                parseSuccess = false;
                return null;
            }

            object[] parameters = new object[] { ConvertValueToString(val), Activator.CreateInstance(tp) };
            bool success = (bool)tryParse.Invoke(null, parameters);
            // 成功返回转换后的值，否则返回类型的默认值
            if (success)
            {
                return parameters[1];
            }

            return propertyInfo.GetValue(obj, null);
        }

        /// <summary>
        /// DataTable转换成List(属性)
        /// </summary>
        /// <param name="dt">表</param>
        /// <returns>泛型List</returns>
        public static object DataTableToList(Type listType, DataTable dt)
        {
            if (dt == null || dt.Rows.Count == 0)
            {
                return null;
            }

            IList list = Activator.CreateInstance(listType) as IList;
            Type type = listType.GetGenericArguments()[0];
            foreach (DataRow row in dt.Rows)
            {
                object t = DataRowToEntiy(type, row);
                if (t != null)
                {
                    list.Add(t);
                }
            }

            return list;
        }

        /// <summary>
        /// DataRow转换成实体(属性)
        /// </summary>
        /// <param name="type">泛型类型</param>
        /// <param name="dr">数据行</param>
        /// <returns>泛型实例</returns>
        public static object DataRowToEntiy(Type type, DataRow dr)
        {
            object model = Activator.CreateInstance(type);
            if (dr == null)
            {
                return model;
            }
            foreach (PropertyInfo property in model.GetType().GetProperties())
            {
                try
                {
                    string propertyName = property.Name;
                    if (dr.Table.Columns.IndexOf(propertyName) < 0 || dr[propertyName] == DBNull.Value)
                    {
                        continue;
                    }

                    object value = dr[propertyName];
                    Type valueType = value.GetType();
                    Type propertyType = property.PropertyType;
                    // 只考虑简单类型
                    // 日期转时间
                    // 枚举类型
                    if (valueType == propertyType || (value is int && propertyType.Name.Equals("boolean", StringComparison.OrdinalIgnoreCase)))
                    {
                        property.SetValue(model, System.Convert.ChangeType(value, propertyType), null);
                    }
                    else if (valueType.Name.Equals("DateTime", StringComparison.OrdinalIgnoreCase)
                        && propertyType.Name.Equals("string", StringComparison.OrdinalIgnoreCase))
                    {
                        property.SetValue(model, System.Convert.ToDateTime(value).ToString(DateTimeFormat), null);
                    }
                    else if (propertyType.IsEnum && (value is int || value is byte))
                    {
                        property.SetValue(model, Enum.ToObject(propertyType, value), null);
                    }
                    else if (value is DataTable)
                    {
                        property.SetValue(model, DataTableToList(propertyType, (DataTable)value), null);
                    }
                    else
                    {
                        bool isCanConvert = false;
                        object objConvert = ConvertType(value, property, model, out isCanConvert);
                        property.SetValue(model, isCanConvert ? objConvert : System.Convert.ChangeType(value, propertyType), null);
                    }
                }
                catch
                {
                }
            }

            return model;
        }

        /// <summary>
        /// 从某个对象复制数据
        /// </summary>
        /// <typeparam name="T">当前对象</typeparam>
        /// <param name="fObj"></param>
        /// <param name="t"></param>
        public static T CopyTo<T>(this object fObj, T t = default)
        {
            if (fObj == null)
            {
                return t;
            }

            Type fType = fObj.GetType();
            Type tType = typeof(T);
            T tObj = t == null ? Activator.CreateInstance<T>() : t;
            foreach (PropertyInfo fieldinfo in fType.GetProperties())
            {
                //Type mtype = fieldinfo.FieldType;
                string fieldname = fieldinfo.Name;
                PropertyInfo tField = tType.GetProperty(fieldname);
                if (tField == null) continue;
                try
                {
                    object item = fieldinfo.GetValue(fObj, null);
                    Type classType = tField.PropertyType;
                    Type valueType = fieldinfo.PropertyType;
                    //只考虑简单类型
                    if (valueType == typeof(DateTime) && classType == typeof(string)) //string类型转DateTime
                    {
                        tField.SetValue(tObj, System.Convert.ToDateTime(item).ToString("yyyy-MM-dd HH:mm:ss"), null);
                    }
                    //只考虑简单类型
                    else if (valueType == typeof(DateTime?))
                    {
                        if (classType == typeof(string))
                        {
                            tField.SetValue(tObj, item == null ? string.Empty : System.Convert.ToDateTime(item).ToString("yyyy-MM-dd HH:mm:ss"), null);
                        }
                        else if (classType == typeof(DateTime) && item != null)
                        {
                            tField.SetValue(tObj, System.Convert.ToDateTime(item), null);
                        }
                        else if (classType == typeof(DateTime?))
                        {
                            tField.SetValue(tObj, item, null);
                        }
                    }
                    else if (classType.IsEnum) //枚举类型
                    {
                        tField.SetValue(tObj, Enum.ToObject(classType, item), null);
                    }

                    else
                    {
                        tField.SetValue(tObj, ChangeType(item, classType), null);
                    }
                }
                catch
                {

                }
            }

            return tObj;
        }

        public static object ChangeType(object value, Type conversion)
        {
            Type t = conversion;

            if (t.IsGenericType && t.GetGenericTypeDefinition().Equals(typeof(Nullable<>)))
            {
                if (value == null)
                {
                    return null;
                }

                t = Nullable.GetUnderlyingType(t);
            }

            return System.Convert.ChangeType(value, t);
        }

        /// <summary>
        /// 日期时间格式化
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public static string ToFormat(this DateTime? time)
        {
            if (time == null)
                return string.Empty;

            return time.Value.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// Object类型转换成Int类型
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static int ToInt(this object obj)
        {
            if (obj == null)
                return 0;
            int.TryParse(obj.ToString(), out int result);
            return result;
        }

        /// <summary>
        /// 列数据转换为DateTime类型
        /// </summary>
        /// <param name="i"></param>
        /// <returns>返回Int类型数据</returns>
        public static DateTime? ToDateTime(this object i)
        {
            if (i == null)
            {
                return null;
            }
            string temp = i.ToString();
            DateTime result;
            DateTime.TryParse(temp, out result);
            return result;
        }

        /// <summary>
        /// Object类型转换成decimal类型
        /// </summary>
        /// <param name="obj">Object类型</param>
        /// <returns>返回decimal类型</returns>
        public static decimal ToDecimal(this object obj)
        {
            if (obj == null)
                return 0;
            decimal result;
            decimal.TryParse(obj.ToString(), out result);
            return result;
        }

        /// <summary>
        /// Object类型转换成Long类型
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static long ToLong(this object obj)
        {
            if (obj == null)
                return 0;
            long result;
            long.TryParse(obj.ToString(), out result);
            return result;
        }

        /// <summary>
        /// Object类型转换成bool类型
        /// </summary>
        /// <param name="obj">任何类型</param>
        /// <returns></returns>
        public static bool ToBool(this object obj)
        {
            if (obj == null)
                return false;
            bool result;
            bool.TryParse(obj.ToString(), out result);
            return result;
        }
    }
}
