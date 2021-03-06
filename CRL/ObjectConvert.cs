using CRL.LambdaQuery.Mapping;
/**
* CRL 快速开发框架 V4.0
* Copyright (c) 2016 Hubro All rights reserved.
* GitHub https://github.com/hubro-xx/CRL3
* 主页 http://www.cnblogs.com/hubro
* 在线文档 http://crl.changqidongli.com/
*/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CRL
{
    internal class ObjectConvert
    {
        static Dictionary<Type, Func<object, object>> nullCheckMethod = new Dictionary<Type, Func<object, object>>();
        /// <summary>
        /// 转化值,并处理默认值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        internal static object CheckNullValue(object value, Type type = null)
        {
            if (type == null && value == null)
            {
                return DBNull.Value;
                //throw new CRLException("至少一项不能为空");
            }
            if (value != null)
            {
                type = value.GetType();
            }
            if (nullCheckMethod.Count == 0)
            {
                nullCheckMethod.Add(typeof(string), (a) =>
                {
                    return a + "";
                });
                nullCheckMethod.Add(typeof(Enum), (a) =>
                {
                    return Convert.ToInt32(a);
                });
                nullCheckMethod.Add(typeof(DateTime), (a) =>
                {
                    DateTime time = (DateTime)a;
                    if (time.Year == 1)
                    {
                        a = DateTime.Now;
                    }
                    return a;
                });
                nullCheckMethod.Add(typeof(byte[]), (a) =>
                {
                    if (a == null)
                        return 0;
                    return a;
                });
                nullCheckMethod.Add(typeof(Guid), (a) =>
                {
                    if (a == null)
                        return Guid.NewGuid().ToString();
                    return a;
                });

            }
            if (nullCheckMethod.ContainsKey(type))
            {
                return nullCheckMethod[type](value);
            }
            if (type.BaseType == typeof(Enum))
            {
                return nullCheckMethod[type.BaseType](value);
            }
            return value;
        }
        static Dictionary<Type, Func<object, object>> convertMethod = new Dictionary<Type, Func<object, object>>();

        /// <summary>
        /// 转换为为强类型
        /// </summary>
        /// <param name="type"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static object ConvertObject(Type type, object value)
        {
            if (convertMethod.Count == 0)
            {
                convertMethod.Add(typeof(byte[]), (a) =>
                {
                    return (byte[])a;
                });
                convertMethod.Add(typeof(Guid), (a) =>
                {
                    return new Guid(a.ToString());
                });
                //convertMethod.Add(typeof(Enum), (a) =>
                //{
                //    return Convert.ToInt32(a);
                //});
            }
            if (type.IsEnum)
            {
                type = type.GetEnumUnderlyingType();
            }
            if (convertMethod.ContainsKey(type))
            {
                return convertMethod[type](value);
            }
            if (type.FullName.StartsWith("System.Nullable"))
            {
                //Nullable<T> 可空属性
                type = type.GenericTypeArguments[0];
            }
            return Convert.ChangeType(value, type);
        }
        
        /// <summary>
        /// 转换为为强类型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        internal static T ConvertObject<T>(object obj)
        {
            if (obj == null)
                return default(T);
            if (obj is DBNull)
                return default(T);
            var type = typeof(T);
            return (T)ConvertObject(type, obj);
        }
        internal struct ActionItem<T>
        {
            public Action<T, object> Set;
            public Action<object, object> Set2;
            public void DoSet(T obj ,object[] values)
            {
                if (Set != null)
                {
                    Set(obj, values[ValueIndex]);
                }
                else
                {
                    Set2(obj, values[ValueIndex]);
                }
            }
            public string Name;
            public int ValueIndex;
            void SetValue(T item, object[] values)
            {
                Set(item, values[ValueIndex]);
            }
        }
        #region 返回object
        /// <summary>
        /// 仅缓存查询用
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="mainType"></param>
        /// <param name="mapping"></param>
        /// <param name="runTime"></param>
        /// <returns></returns>
        internal static List<object> DataReaderToObjectList(DbDataReader reader, Type mainType, List<Attribute.FieldMapping> mapping,out double runTime)
        {
            //rem mainType 不一定为T
            var sw = new Stopwatch();
            sw.Start();
            var list = new List<object>();
            var typeArry = TypeCache.GetTable(mainType).Fields;
            var columns = new Dictionary<string, int>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i).ToLower(), i);
            }
            var actions = new List<ActionItem<object>>();

            object[] values = new object[reader.FieldCount];
            foreach (var mp in mapping)
            {
                var fieldName = mp.QueryName.ToLower();
                var info = typeArry.Find(b => b.MemberName == mp.MappingName);
                if (info == null)
                {
                    continue;
                }
                var action = new ActionItem<object>() { Set2 = info.SetValue, Name = mp.MappingName, ValueIndex = columns[fieldName] };
                columns.Remove(fieldName);
                actions.Add(action);
            }
            while (reader.Read())
            {
                reader.GetValues(values);
                var detailItem = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(mainType);
                foreach (var ac in actions)
                {
                    ac.DoSet(detailItem, values);
                }
                #region 剩下的放索引
                //按IModel算
                if (columns.Count > 0)
                {
                    var model = detailItem as IModel;
                    foreach (var item in columns)
                    {
                        var col = item.Key;
                        var n = col.LastIndexOf("__");
                        if (n == -1)
                        {
                            continue;
                        }
                        var mapingName = col.Substring(n + 2);
                        model.SetIndexData(mapingName, values[item.Value]);
                    }
                }
                #endregion
                list.Add(detailItem);

            }
            reader.Close();
            reader.Dispose();
            sw.Stop();
            runTime = sw.ElapsedMilliseconds;
            //Console.WriteLine("CRL映射用时:" + runTime);
            return list;
        }
        #endregion
        #region 返回指定类型
        /// <summary>
        /// 返回指定类型,支持强类型和匿名类型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reader"></param>
        /// <param name="queryInfo"></param>
        /// <returns></returns>
        internal static List<T> DataReaderToSpecifiedList<T>(DbDataReader reader,QueryInfo<T> queryInfo)
        {
            var objCreater = queryInfo.ObjCreater;
            var mapping = queryInfo.Mapping;
            var list = new List<T>();
            var columns = new Dictionary<string, int>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i).ToLower(), i);
            }
            var actions = new List<ActionItem<T>>();
            object[] values = new object[reader.FieldCount];
            foreach (var mp in mapping)
            {
                var fieldName = mp.QueryName.ToLower();
                var accessor = queryInfo.Reflection.GetAccessor(mp.MappingName);
                if (accessor == null)
                    continue;
                var action = new ActionItem<T>() { Set = accessor.Set, Name = mp.MappingName, ValueIndex = columns[fieldName] };
                columns.Remove(fieldName);
                actions.Add(action);
            }
            while (reader.Read())
            {
                reader.GetValues(values);
                var dataContainer = new DataContainer(values);
                T detailItem = objCreater(dataContainer);
                foreach (var ac in actions)
                {
                    ac.DoSet(detailItem, values);
                }
                #region 剩下的放索引
                //按IModel算
                if (columns.Count > 0)
                {
                    var model = detailItem as IModel;
                    foreach (var item in columns)
                    {
                        var col = item.Key;
                        var n = col.LastIndexOf("__");
                        if (n == -1)
                        {
                            continue;
                        }
                        var mapingName = col.Substring(n + 2);
                        model.SetIndexData(mapingName, values[item.Value]);
                    }
                }
                #endregion
                list.Add(detailItem);
            }
            reader.Close();
            reader.Dispose();
            //Console.WriteLine("CRL映射用时:" + runTime);
            return list;
        }
        #endregion
        /// <summary>
        /// DataRead转为字典
        /// </summary>
        /// <typeparam name="Tkey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="reader"></param>
        /// <returns></returns>
        internal static Dictionary<Tkey, TValue> DataReadToDictionary<Tkey, TValue>(DbDataReader reader)
        {
            var dic = new Dictionary<Tkey, TValue>();
            while (reader.Read())
            {
                object data1 = reader[0];
                object data2 = reader[1];
                //Tkey key = ConvertObject<Tkey>(data1);
                //TValue value = ConvertObject<TValue>(data2);
                Tkey key = (Tkey)data1;
                TValue value = (TValue)data2;
                dic.Add(key, value);
            }
            reader.Close();
            return dic;
        }
        /// <summary>
        /// 将集合转换为主键字典
        /// </summary>
        /// <typeparam name="TItem"></typeparam>
        /// <param name="list"></param>
        /// <returns></returns>
        internal static Dictionary<string, TItem> ConvertToDictionary<TItem>(IEnumerable list) where TItem : IModel
        {
            var dic = new Dictionary<string, TItem>();
            foreach (var item in list)
            {
                var keyValue = (item as IModel).GetpPrimaryKeyValue().ToString();
                if (!dic.ContainsKey(keyValue))
                {
                    dic.Add(keyValue, item as TItem);
                }
            }
            return dic;
        }
    }
}
