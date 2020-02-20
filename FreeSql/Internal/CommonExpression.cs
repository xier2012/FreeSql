﻿using FreeSql.Internal.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FreeSql.DataAnnotations;
using System.Threading;

namespace FreeSql.Internal
{
    public abstract class CommonExpression
    {

        public CommonUtils _common;
        public CommonProvider.AdoProvider _ado => _adoPriv ?? (_adoPriv = _common._orm.Ado as CommonProvider.AdoProvider);
        CommonProvider.AdoProvider _adoPriv;
        public CommonExpression(CommonUtils common)
        {
            _common = common;
        }

        internal const int ReadAnonymousFieldAsCsName = -53129;
        public bool ReadAnonymousField(List<SelectTableInfo> _tables, StringBuilder field, ReadAnonymousTypeInfo parent, ref int index, Expression exp, Func<Expression[], string> getSelectGroupingMapString, List<LambdaExpression> whereCascadeExpression, bool isAllDtoMap)
        {
            Func<ExpTSC> getTSC = () => new ExpTSC { _tables = _tables, getSelectGroupingMapString = getSelectGroupingMapString, tbtype = SelectTableInfoType.From, isQuoteName = true, isDisableDiyParse = false, style = ExpressionStyle.Where, whereCascadeExpression = whereCascadeExpression };
            switch (exp.NodeType)
            {
                case ExpressionType.Quote: return ReadAnonymousField(_tables, field, parent, ref index, (exp as UnaryExpression)?.Operand, getSelectGroupingMapString, whereCascadeExpression, isAllDtoMap);
                case ExpressionType.Lambda: return ReadAnonymousField(_tables, field, parent, ref index, (exp as LambdaExpression)?.Body, getSelectGroupingMapString, whereCascadeExpression, isAllDtoMap);
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                    parent.DbField = $"-({ExpressionLambdaToSql(exp, getTSC())})";
                    field.Append(", ").Append(parent.DbField);
                    if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
                    else if (index == ReadAnonymousFieldAsCsName && string.IsNullOrEmpty(parent.CsName) == false) field.Append(_common.FieldAsAlias(parent.CsName));
                    return false;
                case ExpressionType.Convert: return ReadAnonymousField(_tables, field, parent, ref index, (exp as UnaryExpression)?.Operand, getSelectGroupingMapString, whereCascadeExpression, isAllDtoMap);
                case ExpressionType.Constant:
                    var constExp = exp as ConstantExpression;
                    //处理自定义SQL语句，如： ToList(new { 
                    //	ccc = "now()", 
                    //	partby = "sum(num) over(PARTITION BY server_id,os,rid,chn order by id desc)"
                    //})，有缺点即 ccc partby 接受类型都是 string，可配合 Convert.ToXxx 类型转换，请看下面的兼容
                    if (constExp.Type.FullName == "System.String")
                    {
                        var constExpValue = constExp.Value?.ToString() ?? "NULL";
                        if (constExpValue == string.Empty) constExpValue = _common.FormatSql("{0}", "");
                        parent.DbField = constExpValue;
                    }
                    else
                        parent.DbField = _common.FormatSql("{0}", constExp?.Value);
                    field.Append(", ").Append(parent.DbField);
                    if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
                    else if (index == ReadAnonymousFieldAsCsName && string.IsNullOrEmpty(parent.CsName) == false) field.Append(_common.FieldAsAlias(parent.CsName));
                    return false;
                case ExpressionType.Call:
                    var callExp = exp as MethodCallExpression;
                    //处理自定义SQL语句，如： ToList(new { 
                    //	ccc = Convert.ToDateTime("now()"), 
                    //	partby = Convert.ToDecimal("sum(num) over(PARTITION BY server_id,os,rid,chn order by id desc)")
                    //})
                    if (callExp.Method?.DeclaringType.FullName == "System.Convert" &&
                        callExp.Method.Name.StartsWith("To") &&
                        callExp.Arguments[0].NodeType == ExpressionType.Constant &&
                        callExp.Arguments[0].Type.FullName == "System.String")
                        parent.DbField = (callExp.Arguments[0] as ConstantExpression).Value?.ToString() ?? "NULL";
                    else
                        parent.DbField = ExpressionLambdaToSql(exp, getTSC());
                    field.Append(", ").Append(parent.DbField);
                    if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
                    else if (index == ReadAnonymousFieldAsCsName && string.IsNullOrEmpty(parent.CsName) == false) field.Append(_common.FieldAsAlias(parent.CsName));
                    return false;
                case ExpressionType.Parameter:
                case ExpressionType.MemberAccess:
                    if (_common.GetTableByEntity(exp.Type) != null)
                    { //加载表所有字段
                        var map = new List<SelectColumnInfo>();
                        ExpressionSelectColumn_MemberAccess(_tables, map, SelectTableInfoType.From, exp, true, getSelectGroupingMapString);
                        var tb = parent.Table = map.First().Table.Table;
                        parent.CsType = tb.Type;
                        parent.Consturctor = tb.Type.InternalGetTypeConstructor0OrFirst();
                        parent.IsEntity = true;
                        for (var idx = 0; idx < map.Count; idx++)
                        {
                            var child = new ReadAnonymousTypeInfo
                            {
                                Property = tb.Properties.TryGetValue(map[idx].Column.CsName, out var tryprop) ? tryprop : tb.Type.GetProperty(map[idx].Column.CsName, BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance),
                                CsName = map[idx].Column.CsName,
                                DbField = $"{map[idx].Table.Alias}.{_common.QuoteSqlName(map[idx].Column.Attribute.Name)}",
                                CsType = map[idx].Column.CsType,
                                MapType = map[idx].Column.Attribute.MapType
                            };
                            field.Append(", ").Append(_common.QuoteReadColumn(child.CsType, child.MapType, child.DbField));
                            if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
                            parent.Childs.Add(child);
                        }
                    }
                    else
                    {
                        parent.CsType = exp.Type;
                        parent.DbField = ExpressionLambdaToSql(exp, getTSC());
                        field.Append(", ").Append(parent.DbField);
                        if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
                        else if (index == ReadAnonymousFieldAsCsName && string.IsNullOrEmpty(parent.CsName) == false) field.Append(_common.FieldAsAlias(parent.CsName));
                        parent.MapType = SearchColumnByField(_tables, null, parent.DbField)?.Attribute.MapType ?? exp.Type;
                        return false;
                    }
                    return false;
                case ExpressionType.MemberInit:
                    var initExp = exp as MemberInitExpression;
                    parent.CsType = initExp.Type;
                    parent.Consturctor = initExp.NewExpression.Constructor;
                    if (initExp.NewExpression?.Arguments.Count > 0)
                    {
                        //处理构造参数
                        for (var a = 0; a < initExp.NewExpression.Arguments.Count; a++)
                        {
                            var child = new ReadAnonymousTypeInfo
                            {
                                Property = null,
                                CsName = initExp.NewExpression.Members != null ? initExp.NewExpression.Members[a].Name : (initExp.NewExpression.Arguments[a] as MemberExpression)?.Member.Name,
                                CsType = initExp.NewExpression.Arguments[a].Type,
                                MapType = initExp.NewExpression.Arguments[a].Type
                            };
                            parent.Childs.Add(child);
                            ReadAnonymousField(_tables, field, child, ref index, initExp.NewExpression.Arguments[a], getSelectGroupingMapString, whereCascadeExpression, false);
                        }
                    }
                    else if (isAllDtoMap && _tables != null && _tables.Any() && initExp.NewExpression.Type != _tables.FirstOrDefault().Table.Type)
                    {
                        //dto 映射
                        var dtoProps = initExp.NewExpression.Type.GetPropertiesDictIgnoreCase().Values;
                        foreach (var dtoProp in dtoProps)
                        {
                            foreach (var dtTb in _tables)
                            {
                                if (dtTb.Table.ColumnsByCs.TryGetValue(dtoProp.Name, out var trydtocol) == false) continue;
                                if (trydtocol.Attribute.IsIgnore == true) continue;

                                var child = new ReadAnonymousTypeInfo
                                {
                                    Property = dtoProp,
                                    CsName = dtoProp.Name,
                                    CsType = trydtocol.CsType, // dtoProp.PropertyType,
                                    MapType = trydtocol.Attribute.MapType
                                };
                                parent.Childs.Add(child);
                                if (dtTb.Parameter != null)
                                    ReadAnonymousField(_tables, field, child, ref index, Expression.Property(dtTb.Parameter, dtTb.Table.Properties[trydtocol.CsName]), getSelectGroupingMapString, whereCascadeExpression, isAllDtoMap);
                                else
                                {
                                    child.DbField = $"{dtTb.Alias}.{_common.QuoteSqlName(trydtocol.Attribute.Name)}";
                                    field.Append(", ").Append(child.DbField);
                                    if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
                                }
                                break;
                            }
                        }
                    }
                    if (initExp.Bindings?.Count > 0)
                    {
                        //指定 dto映射
                        for (var a = 0; a < initExp.Bindings.Count; a++)
                        {
                            var initAssignExp = (initExp.Bindings[a] as MemberAssignment);
                            if (initAssignExp == null) continue;
                            var child = new ReadAnonymousTypeInfo
                            {
                                Property = initExp.Type.GetProperty(initExp.Bindings[a].Member.Name, BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance),
                                CsName = initExp.Bindings[a].Member.Name,
                                CsType = initAssignExp.Expression.Type,
                                MapType = initAssignExp.Expression.Type
                            };
                            parent.Childs.Add(child);
                            ReadAnonymousField(_tables, field, child, ref index, initAssignExp.Expression, getSelectGroupingMapString, whereCascadeExpression, false);
                        }
                    }
                    if (parent.Childs.Any() == false) throw new Exception($"映射异常：{initExp.NewExpression.Type.Name} 没有一个属性名相同");
                    return true;
                case ExpressionType.New:
                    var newExp = exp as NewExpression;
                    parent.CsType = newExp.Type;
                    parent.Consturctor = newExp.Constructor;
                    if (newExp.Arguments?.Count > 0 &&
                        (
                            newExp.Type.IsAnonymousType() ||
                            newExp.Arguments.Any(a =>
                            {
                                if (a.NodeType != ExpressionType.Constant) return true;
                                var constVal = (a as ConstantExpression)?.Value;
                                if (constVal == null) return true;
                                if (object.Equals(constVal, a.Type.CreateInstanceGetDefaultValue()) == false) return true;
                                return false;
                            })
                        ))
                    {
                        //处理构造参数
                        for (var a = 0; a < newExp.Arguments.Count; a++)
                        {
                            var child = new ReadAnonymousTypeInfo
                            {
                                Property = null,
                                CsName = newExp.Members != null ? newExp.Members[a].Name : (newExp.Arguments[a] as MemberExpression)?.Member.Name,
                                CsType = newExp.Arguments[a].Type,
                                MapType = newExp.Arguments[a].Type
                            };
                            parent.Childs.Add(child);
                            ReadAnonymousField(_tables, field, child, ref index, newExp.Arguments[a], getSelectGroupingMapString, whereCascadeExpression, false);
                        }
                    }
                    else
                    {
                        parent.IsDefaultCtor = true;
                        //dto 映射
                        var dtoProps2 = newExp.Type.GetPropertiesDictIgnoreCase().Values;
                        foreach (var dtoProp in dtoProps2)
                        {
                            foreach (var dtTb in _tables)
                            {
                                if (dtTb.Table.ColumnsByCs.TryGetValue(dtoProp.Name, out var trydtocol) == false) continue;
                                if (trydtocol.Attribute.IsIgnore == true) continue;

                                var child = new ReadAnonymousTypeInfo
                                {
                                    Property = dtoProp,
                                    CsName = dtoProp.Name,
                                    CsType = trydtocol.CsType, //dtoProp.PropertyType,
                                    MapType = trydtocol.Attribute.MapType
                                };
                                parent.Childs.Add(child);
                                if (dtTb.Parameter != null)
                                    ReadAnonymousField(_tables, field, child, ref index, Expression.Property(dtTb.Parameter, dtTb.Table.Properties[trydtocol.CsName]), getSelectGroupingMapString, whereCascadeExpression, isAllDtoMap);
                                else
                                {
                                    child.DbField = $"{dtTb.Alias}.{_common.QuoteSqlName(trydtocol.Attribute.Name)}";
                                    field.Append(", ").Append(child.DbField);
                                    if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
                                }
                                break;
                            }
                        }
                    }
                    if (parent.Childs.Any() == false) throw new Exception($"映射异常：{newExp.Type.Name} 没有一个属性名相同");
                    return true;
            }
            parent.DbField = $"({ExpressionLambdaToSql(exp, getTSC())})";
            field.Append(", ").Append(parent.DbField);
            if (index >= 0) field.Append(_common.FieldAsAlias($"as{++index}"));
            else if (index == ReadAnonymousFieldAsCsName && string.IsNullOrEmpty(parent.CsName) == false) field.Append(_common.FieldAsAlias(parent.CsName));
            return false;
        }
        public object ReadAnonymous(ReadAnonymousTypeInfo parent, DbDataReader dr, ref int index, bool notRead, ReadAnonymousDbValueRef dbValue)
        {
            if (parent.Childs.Any() == false)
            {
                if (notRead)
                {
                    ++index;
                    if (parent.Property != null) 
                        return Utils.GetDataReaderValue(parent.Property.PropertyType, null);
                    return Utils.GetDataReaderValue(parent.CsType, null);
                }
                object objval = dr.GetValue(++index);
                if (dbValue != null) dbValue.DbValue = objval == DBNull.Value ? null : objval;
                if (parent.CsType != parent.MapType) 
                    objval = Utils.GetDataReaderValue(parent.MapType, objval);
                objval = Utils.GetDataReaderValue(parent.CsType, objval);
                if (parent.Property != null && parent.CsType != parent.Property.PropertyType) 
                    objval = Utils.GetDataReaderValue(parent.Property.PropertyType, objval);
                return objval;
            }
            var ctorParmsLength = 0;
            object ret;
            if (parent.IsDefaultCtor || parent.IsEntity || (ctorParmsLength = parent.Consturctor.GetParameters()?.Length ?? 0) == 0)
                ret = parent.CsType?.CreateInstanceGetDefaultValue() ?? parent.Consturctor.Invoke(null);
            else
            {
                var ctorParms = new object[ctorParmsLength];
                for (var c = 0; c < ctorParmsLength; c++)
                    ctorParms[c] = ReadAnonymous(parent.Childs[c], dr, ref index, notRead, null);
                ret = parent.Consturctor.Invoke(ctorParms);
            }

            var isnull = notRead;
            for (var b = ctorParmsLength; b < parent.Childs.Count; b++)
            {
                var prop = parent.Childs[b].Property;
                var dbval = parent.IsEntity ? new ReadAnonymousDbValueRef() : null;
                var objval = ReadAnonymous(parent.Childs[b], dr, ref index, notRead, dbval);
                if (isnull == false && parent.IsEntity && dbval.DbValue == null && parent.Table != null && parent.Table.ColumnsByCs.TryGetValue(parent.Childs[b].CsName, out var trycol) && trycol.Attribute.IsPrimary)
                    isnull = true;
                if (isnull == false && prop.CanWrite)
                    prop.SetValue(ret, objval, null);
            }
            return isnull ? null : ret;
        }
        public class ReadAnonymousDbValueRef
        {
            public object DbValue { get; set; }
        }

        public ColumnInfo SearchColumnByField(List<SelectTableInfo> _tables, TableInfo currentTable, string field)
        {
            if (_tables != null)
            {
                var testCol = _common.TrimQuoteSqlName(field).Split(new[] { '.' }, 2);
                if (testCol.Length == 2)
                {
                    var testTb = _tables.Where(a => a.Alias == testCol[0]).ToArray();
                    if (testTb.Length == 1 && testTb[0].Table.Columns.TryGetValue(testCol[1], out var trytstcol))
                        return trytstcol;
                }
            }
            if (currentTable != null)
            {
                var testCol = _common.TrimQuoteSqlName(field);
                if (currentTable.Columns.TryGetValue(testCol, out var trytstcol))
                    return trytstcol;
            }
            return null;
        }

        public string ExpressionSelectColumn_MemberAccess(List<SelectTableInfo> _tables, List<SelectColumnInfo> _selectColumnMap, SelectTableInfoType tbtype, Expression exp, bool isQuoteName, Func<Expression[], string> getSelectGroupingMapString)
        {
            return ExpressionLambdaToSql(exp, new ExpTSC { _tables = _tables, _selectColumnMap = _selectColumnMap, getSelectGroupingMapString = getSelectGroupingMapString, tbtype = tbtype, isQuoteName = isQuoteName, isDisableDiyParse = false, style = ExpressionStyle.SelectColumns });
        }

        public string[] ExpressionSelectColumns_MemberAccess_New_NewArrayInit(List<SelectTableInfo> _tables, Expression exp, bool isQuoteName, Func<Expression[], string> getSelectGroupingMapString)
        {
            switch (exp?.NodeType)
            {
                case ExpressionType.Quote: return ExpressionSelectColumns_MemberAccess_New_NewArrayInit(_tables, (exp as UnaryExpression)?.Operand, isQuoteName, getSelectGroupingMapString);
                case ExpressionType.Lambda: return ExpressionSelectColumns_MemberAccess_New_NewArrayInit(_tables, (exp as LambdaExpression)?.Body, isQuoteName, getSelectGroupingMapString);
                case ExpressionType.Convert: return ExpressionSelectColumns_MemberAccess_New_NewArrayInit(_tables, (exp as UnaryExpression)?.Operand, isQuoteName, getSelectGroupingMapString);
                case ExpressionType.Constant: return new[] { ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, isQuoteName, getSelectGroupingMapString) };
                case ExpressionType.MemberAccess: return new[] { ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, isQuoteName, getSelectGroupingMapString) };
                case ExpressionType.New:
                    var newExp = exp as NewExpression;
                    if (newExp == null) break;
                    var newExpMembers = new string[newExp.Members.Count];
                    for (var a = 0; a < newExpMembers.Length; a++) newExpMembers[a] = ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, newExp.Arguments[a], isQuoteName, getSelectGroupingMapString);
                    return newExpMembers;
                case ExpressionType.NewArrayInit:
                    var newArr = exp as NewArrayExpression;
                    if (newArr == null) break;
                    var newArrMembers = new List<string>();
                    foreach (var newArrExp in newArr.Expressions) newArrMembers.AddRange(ExpressionSelectColumns_MemberAccess_New_NewArrayInit(_tables, newArrExp, isQuoteName, getSelectGroupingMapString));
                    return newArrMembers.ToArray();
            }
            return new string[0];
        }

        static readonly Dictionary<ExpressionType, string> dicExpressionOperator = new Dictionary<ExpressionType, string>() {
            { ExpressionType.OrElse, "OR" },
            { ExpressionType.Or, "|" },
            { ExpressionType.AndAlso, "AND" },
            { ExpressionType.And, "&" },
            { ExpressionType.GreaterThan, ">" },
            { ExpressionType.GreaterThanOrEqual, ">=" },
            { ExpressionType.LessThan, "<" },
            { ExpressionType.LessThanOrEqual, "<=" },
            { ExpressionType.NotEqual, "<>" },
            { ExpressionType.Add, "+" },
            { ExpressionType.Subtract, "-" },
            { ExpressionType.Multiply, "*" },
            { ExpressionType.Divide, "/" },
            { ExpressionType.Modulo, "%" },
            { ExpressionType.Equal, "=" },
        };

        public string ExpressionWhereLambdaNoneForeignObject(List<SelectTableInfo> _tables, TableInfo table, List<SelectColumnInfo> _selectColumnMap, Expression exp, Func<Expression[], string> getSelectGroupingMapString, List<DbParameter> dbParams)
        {
            var sql = ExpressionLambdaToSql(exp, new ExpTSC { _tables = _tables, _selectColumnMap = _selectColumnMap, getSelectGroupingMapString = getSelectGroupingMapString, tbtype = SelectTableInfoType.From, isQuoteName = true, isDisableDiyParse = false, style = ExpressionStyle.Where, currentTable = table, dbParams = dbParams });
            return GetBoolString(exp, sql);
        }

        public string ExpressionWhereLambda(List<SelectTableInfo> _tables, Expression exp, Func<Expression[], string> getSelectGroupingMapString, List<LambdaExpression> whereCascadeExpression, List<DbParameter> dbParams)
        {
            var sql = ExpressionLambdaToSql(exp, new ExpTSC { _tables = _tables, getSelectGroupingMapString = getSelectGroupingMapString, tbtype = SelectTableInfoType.From, isQuoteName = true, isDisableDiyParse = false, style = ExpressionStyle.Where, whereCascadeExpression = whereCascadeExpression, dbParams = dbParams });
            return GetBoolString(exp, sql);
        }
        static ConcurrentDictionary<string, Regex> dicRegexAlias = new ConcurrentDictionary<string, Regex>();
        public void ExpressionJoinLambda(List<SelectTableInfo> _tables, SelectTableInfoType tbtype, Expression exp, Func<Expression[], string> getSelectGroupingMapString, List<LambdaExpression> whereCascadeExpression)
        {
            var tbidx = _tables.Count;
            var sql = ExpressionLambdaToSql(exp, new ExpTSC { _tables = _tables, getSelectGroupingMapString = getSelectGroupingMapString, tbtype = tbtype, isQuoteName = true, isDisableDiyParse = false, style = ExpressionStyle.Where, whereCascadeExpression = whereCascadeExpression });
            sql = GetBoolString(exp, sql);

            if (_tables.Count > tbidx)
            {
                _tables[tbidx].Type = tbtype;
                _tables[tbidx].On = sql;
                for (var a = tbidx + 1; a < _tables.Count; a++)
                    _tables[a].Type = SelectTableInfoType.From;
            }
            else
            {
                var find = _tables.Where((a, c) => c > 0 &&
                    (a.Type == tbtype || a.Type == SelectTableInfoType.From) &&
                    string.IsNullOrEmpty(a.On) &&
                    dicRegexAlias.GetOrAdd(a.Alias, alias => new Regex($@"\b{alias}\.", RegexOptions.Compiled)).IsMatch(sql)).LastOrDefault();
                if (find != null)
                {
                    find.Type = tbtype;
                    find.On = sql;
                }
            }
        }
        static ConcurrentDictionary<Type, MethodInfo> _dicExpressionLambdaToSqlAsSelectMethodInfo = new ConcurrentDictionary<Type, MethodInfo>();
        static ConcurrentDictionary<Type, MethodInfo> _dicExpressionLambdaToSqlAsSelectWhereMethodInfo = new ConcurrentDictionary<Type, MethodInfo>();
        static ConcurrentDictionary<Type, MethodInfo> _dicExpressionLambdaToSqlAsSelectWhereSqlMethodInfo = new ConcurrentDictionary<Type, MethodInfo>();
        static ConcurrentDictionary<Type, MethodInfo> _dicExpressionLambdaToSqlAsSelectAnyMethodInfo = new ConcurrentDictionary<Type, MethodInfo>();
        internal static ConcurrentDictionary<Type, PropertyInfo> _dicNullableValueProperty = new ConcurrentDictionary<Type, PropertyInfo>();
        static ConcurrentDictionary<Type, Expression> _dicFreeSqlGlobalExtensionsAsSelectExpression = new ConcurrentDictionary<Type, Expression>();
        static MethodInfo MethodDateTimeSubtractDateTime = typeof(DateTime).GetMethod("Subtract", new Type[] { typeof(DateTime) });
        static MethodInfo MethodDateTimeSubtractTimeSpan = typeof(DateTime).GetMethod("Subtract", new Type[] { typeof(TimeSpan) });
        static MethodInfo MethodMathFloor = typeof(Math).GetMethod("Floor", new Type[] { typeof(double) });

        public string GetBoolString(Expression exp, string sql)
        {
            var isBool = exp.Type.NullableTypeOrThis() == typeof(bool);
            if (exp.NodeType == ExpressionType.MemberAccess && isBool && sql.Contains(" IS ") == false && sql.Contains(" = ") == false)
                return $"{sql} = {formatSql(true, null, null, null)}";
            if (isBool)
                return GetBoolString(sql);
            return sql;
        }
        static string GetBoolString(string sql)
        {
            switch (sql)
            {
                case "1":
                case "'t'": return "1=1";
                case "0":
                case "'f'": return "1=2";
                default: return sql;
            }
        }
        public string ExpressionBinary(string oper, Expression leftExp, Expression rightExp, ExpTSC tsc)
        {
            if (leftExp.NodeType == ExpressionType.Call &&
                rightExp.NodeType == ExpressionType.Constant &&
                new[] { "=", "<>" }.Contains(oper))
            {
                var leftExpCall = leftExp as MethodCallExpression;
                //vb 语法，将字符串比较转换为了 CompareString
                if (leftExpCall.Method.Name == "CompareString" &&
                    leftExpCall.Method.DeclaringType?.FullName == "Microsoft.VisualBasic.CompilerServices.Operators" &&
                    leftExpCall.Arguments.Count == 3 &&
                    leftExpCall.Arguments[2].Type == typeof(bool) &&
                    rightExp.Type == typeof(int) &&
                    (int)(rightExp as ConstantExpression).Value == 0)
                    return ExpressionBinary(oper, leftExpCall.Arguments[0], leftExpCall.Arguments[1], tsc);
            }
            switch (oper)
            {
                case "OR":
                case "|":
                case "&":
                case "+":
                case "-":
                    if (oper == "+" && (leftExp.Type == typeof(string) || rightExp.Type == typeof(string)))
                        return _common.StringConcat(new[] { ExpressionLambdaToSql(leftExp, tsc), ExpressionLambdaToSql(rightExp, tsc) }, new[] { leftExp.Type, rightExp.Type });
                    if (oper == "-" && leftExp.Type.NullableTypeOrThis() == typeof(DateTime))
                    {
                        if (rightExp.Type.NullableTypeOrThis() == typeof(DateTime))
                            return ExpressionLambdaToSql(Expression.Call(leftExp, MethodDateTimeSubtractDateTime, rightExp), tsc);
                        if (rightExp.Type.NullableTypeOrThis() == typeof(TimeSpan))
                            return ExpressionLambdaToSql(Expression.Call(leftExp, MethodDateTimeSubtractTimeSpan, rightExp), tsc);
                    }
                    return $"({ExpressionLambdaToSql(leftExp, tsc)} {oper} {ExpressionLambdaToSql(rightExp, tsc)})";
            }

            Type oldMapType = null;
            var left = ExpressionLambdaToSql(leftExp, tsc);
            var leftMapColumn = SearchColumnByField(tsc._tables, tsc.currentTable, left);
            var isLeftMapType = leftMapColumn != null && new[] { "AND", "OR" }.Contains(oper) == false && (leftMapColumn.Attribute.MapType != rightExp.Type || leftMapColumn.CsType != rightExp.Type);
            ColumnInfo rightMapColumn = null;
            var isRightMapType = false;
            if (isLeftMapType) oldMapType = tsc.SetMapTypeReturnOld(leftMapColumn.Attribute.MapType);
            
            var right = ExpressionLambdaToSql(rightExp, tsc);
            if (right != "NULL" && isLeftMapType)
            {
                var enumType = leftMapColumn.CsType.NullableTypeOrThis();
                if (enumType.IsEnum)
                    right = formatSql(Enum.Parse(enumType, right.StartsWith("N'") ? right.Substring(1).Trim('\'') : right.Trim('\'')), leftMapColumn.Attribute.MapType, leftMapColumn, tsc.dbParams);
            }
            if (leftMapColumn == null)
            {
                rightMapColumn = SearchColumnByField(tsc._tables, tsc.currentTable, right);
                isRightMapType = rightMapColumn != null && new[] { "AND", "OR" }.Contains(oper) == false && (rightMapColumn.Attribute.MapType != leftExp.Type || rightMapColumn.CsType != leftExp.Type);
                if (isRightMapType)
                {
                    oldMapType = tsc.SetMapTypeReturnOld(rightMapColumn.Attribute.MapType);
                    left = ExpressionLambdaToSql(leftExp, tsc);
                    if (left != "NULL" && isRightMapType)
                    {
                        var enumType = rightMapColumn.CsType.NullableTypeOrThis();
                        if (enumType.IsEnum)
                            left = formatSql(Enum.Parse(enumType, left.StartsWith("N'") ? left.Substring(1).Trim('\'') : left.Trim('\'')), rightMapColumn.Attribute.MapType, rightMapColumn, tsc.dbParams);
                    }
                }
            }
            if (leftExp.Type.NullableTypeOrThis() == typeof(bool) && (leftExp.NodeType != ExpressionType.MemberAccess && rightExp.NodeType != ExpressionType.MemberAccess))
            {
                if (oper == "=")
                {
                    var trueVal = formatSql(true, null, null, null);
                    var falseVal = formatSql(false, null, null, null);
                    if (left == trueVal) return right;
                    else if (left == falseVal) return $"not({right})";
                    else if (right == trueVal) return left;
                    else if (right == falseVal) return $"not({left})";
                }
                else if (oper == "<>")
                {
                    var trueVal = formatSql(true, null, null, null);
                    var falseVal = formatSql(false, null, null, null);
                    if (left == trueVal) return $"not({right})";
                    else if (left == falseVal) return right;
                    else if (right == trueVal) return $"not({left})";
                    else if (right == falseVal) return left;
                }
            }
            if (left == "NULL")
            {
                var tmp = right;
                right = left;
                left = tmp;
            }
            if (right == "NULL") oper = oper == "=" ? " IS " : " IS NOT ";
            switch (oper)
            {
                case "%": return _common.Mod(left, right, leftExp.Type, rightExp.Type);
                case "/":
                    if (leftExp.Type.IsIntegerType() && rightExp.Type.IsIntegerType()) return _common.Div(left, right, leftExp.Type, rightExp.Type);
                    break;
                case "AND":
                case "OR":
                    if (leftMapColumn != null) left = $"{left} = {formatSql(true, null, null, null)}";
                    else left = GetBoolString(left);
                    if (rightMapColumn != null) right = $"{right} = {formatSql(true, null, null, null)}";
                    else right = GetBoolString(right);
                    break;
            }
            tsc.SetMapColumnTmp(null).SetMapTypeReturnOld(oldMapType);
            return $"{left} {oper} {right}";
        }
        static ConcurrentDictionary<Type, bool> _dicTypeExistsExpressionCallAttribute = new ConcurrentDictionary<Type, bool>();
        static ConcurrentDictionary<Type, ConcurrentDictionary<string, bool>> _dicMethodExistsExpressionCallAttribute = new ConcurrentDictionary<Type, ConcurrentDictionary<string, bool>>();
        static ConcurrentDictionary<Type, FieldInfo[]> _dicTypeExpressionCallClassContextFields = new ConcurrentDictionary<Type, FieldInfo[]>();
        public string ExpressionLambdaToSql(Expression exp, ExpTSC tsc)
        {
            if (exp == null) return "";
            if (tsc.dbParams != null && tsc.mapColumnTmp != null && tsc.mapColumnTmp.CsType.NullableTypeOrThis() != exp.Type) tsc.SetMapColumnTmp(null);
            if (tsc.isDisableDiyParse == false && _common._orm.Aop.ParseExpression != null)
            {
                var args = new Aop.ParseExpressionEventArgs(exp, ukexp => ExpressionLambdaToSql(ukexp, tsc.CloneDisableDiyParse()));
                _common._orm.Aop.ParseExpression?.Invoke(this, args);
                if (string.IsNullOrEmpty(args.Result) == false) return args.Result;
            }
            switch (exp.NodeType)
            {
                case ExpressionType.Not:
                    var notExp = (exp as UnaryExpression)?.Operand;
                    if (notExp.NodeType == ExpressionType.MemberAccess)
                    {
                        var notBody = ExpressionLambdaToSql(notExp, tsc);
                        if (notBody.Contains(" IS NULL")) return notBody.Replace(" IS NULL", " IS NOT NULL");
                        if (notBody.Contains(" IS NOT NULL")) return notBody.Replace(" IS NOT NULL", " IS NULL");
                        if (notBody.Contains("=")) return notBody.Replace("=", "!=");
                        if (notBody.Contains("!=")) return notBody.Replace("!=", "=");
                        return $"{notBody} = {formatSql(false, null, null, null)}";
                    }
                    return $"not({ExpressionLambdaToSql(notExp, tsc)})";
                case ExpressionType.Quote: return ExpressionLambdaToSql((exp as UnaryExpression)?.Operand, tsc);
                case ExpressionType.Lambda: return ExpressionLambdaToSql((exp as LambdaExpression)?.Body, tsc);
                case ExpressionType.TypeAs:
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    //var othercExp = ExpressionLambdaToSqlOther(exp, tsc);
                    //if (string.IsNullOrEmpty(othercExp) == false) return othercExp;
                    return ExpressionLambdaToSql((exp as UnaryExpression)?.Operand, tsc);
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked: return "-" + ExpressionLambdaToSql((exp as UnaryExpression)?.Operand, tsc);
                case ExpressionType.Constant: return formatSql((exp as ConstantExpression)?.Value, tsc.mapType, tsc.mapColumnTmp, null);
                case ExpressionType.Conditional:
                    var condExp = exp as ConditionalExpression;
                    return _common.IIF(ExpressionLambdaToSql(condExp.Test, tsc), ExpressionLambdaToSql(condExp.IfTrue, tsc), ExpressionLambdaToSql(condExp.IfFalse, tsc));
                case ExpressionType.Call:
                    tsc.mapType = null;
                    var exp3 = exp as MethodCallExpression;
                    if (exp3.Object == null && (
                        _dicTypeExistsExpressionCallAttribute.GetOrAdd(exp3.Method.DeclaringType, dttp => dttp.GetCustomAttributes(typeof(ExpressionCallAttribute), true).Any()) ||
                        exp3.Method.GetCustomAttributes(typeof(ExpressionCallAttribute), true).Any()
                        ))
                    {
                        var ecc = new ExpressionCallContext { 
                            _commonExp = this, 
                            _tsc = tsc, 
                            DataType = _ado.DataType, 
                            UserParameters = tsc.dbParams == null ? null : new List<DbParameter>(), 
                            FormatSql = obj => formatSql(obj, null, null, null) 
                        };
                        var exp3MethodParams = exp3.Method.GetParameters();
                        var dbParamsIndex = tsc.dbParams?.Count;
                        ecc.RawExpression.Add(exp3MethodParams[0].Name, exp3.Arguments[0]);
                        ecc.ParsedContent.Add(exp3MethodParams[0].Name, exp3MethodParams[0].GetCustomAttributes(typeof(RawValueAttribute), true).Any() ? null : ExpressionLambdaToSql(exp3.Arguments[0], tsc));
                        if (tsc.dbParams?.Count > dbParamsIndex) ecc.DbParameter = tsc.dbParams.Last();
                        List<DbParameter> oldDbParams = tsc.SetDbParamsReturnOld(null);
                        for (var a = 1; a < exp3.Arguments.Count; a++)
                            if (exp3.Arguments[a].Type != typeof(ExpressionCallContext))
                            {
                                ecc.RawExpression.Add(exp3MethodParams[a].Name, exp3.Arguments[a]);
                                ecc.ParsedContent.Add(exp3MethodParams[a].Name, exp3MethodParams[a].GetCustomAttributes(typeof(RawValueAttribute), true).Any() ? null : ExpressionLambdaToSql(exp3.Arguments[a], tsc));
                            }
                        tsc.SetDbParamsReturnOld(oldDbParams);

                        var exp3InvokeParams = new object[exp3.Arguments.Count];
                        for (var a = 0; a < exp3.Arguments.Count; a++)
                        {
                            if (exp3.Arguments[a].Type != typeof(ExpressionCallContext))
                            {
                                var eccContent = ecc.ParsedContent[exp3MethodParams[a].Name];
                                if (eccContent == null)
                                    exp3InvokeParams[a] = Expression.Lambda(exp3.Arguments[a]).Compile().DynamicInvoke();
                                else
                                    exp3InvokeParams[a] = Utils.GetDataReaderValue(exp3.Arguments[a].Type,
                                        eccContent.StartsWith("N'") ?
                                        eccContent.Substring(1).Trim('\'').Replace("''", "'") :
                                        eccContent.Trim('\'').Replace("''", "'"));// exp3.Arguments[a].Type.CreateInstanceGetDefaultValue();
                            }
                            else
                                exp3InvokeParams[a] = ecc;
                        }
                        var eccFields = _dicTypeExpressionCallClassContextFields.GetOrAdd(exp3.Method.DeclaringType, dttp => 
                            dttp.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Static).Where(a => a.FieldType == typeof(ThreadLocal<ExpressionCallContext>)).ToArray());
                        if (eccFields.Any() == false)
                            throw new Exception($"自定义表达式解析错误：类型 {exp3.Method.DeclaringType} 需要定义 static ThreadLocal<ExpressionCallContext> 字段、字段、字段（重要三次提醒）");
                        foreach (var eccField in eccFields)
                            typeof(ThreadLocal<ExpressionCallContext>).GetProperty("Value").SetValue(eccField.GetValue(null), ecc, null);
                        try
                        {
                            var sqlRet = exp3.Method.Invoke(null, exp3InvokeParams);
                            if (string.IsNullOrEmpty(ecc.Result) && sqlRet is string) ecc.Result = string.Concat(sqlRet);
                            if (string.IsNullOrEmpty(ecc.Result)) ecc.Result = ecc.ParsedContent[exp3MethodParams[0].Name];
                            if (ecc.UserParameters?.Any() == true) tsc.dbParams?.AddRange(ecc.UserParameters);
                            return ecc.Result;
                        }
                        finally
                        {
                            foreach (var eccField in eccFields)
                                typeof(ThreadLocal<ExpressionCallContext>).GetProperty("Value").SetValue(eccField.GetValue(null), null, null);
                        }
                    }
                    var callType = exp3.Object?.Type ?? exp3.Method.DeclaringType;
                    string other3Exp = null;
                    switch (callType.FullName)
                    {
                        case "System.String": other3Exp = ExpressionLambdaToSqlCallString(exp3, tsc); break;
                        case "System.Math": other3Exp = ExpressionLambdaToSqlCallMath(exp3, tsc); break;
                        case "System.DateTime": other3Exp = ExpressionLambdaToSqlCallDateTime(exp3, tsc); break;
                        case "System.TimeSpan": other3Exp = ExpressionLambdaToSqlCallTimeSpan(exp3, tsc); break;
                        case "System.Convert": other3Exp = ExpressionLambdaToSqlCallConvert(exp3, tsc); break;
                    }
                    if (string.IsNullOrEmpty(other3Exp) == false) return other3Exp;
                    if (exp3.Method.Name == "Equals" && exp3.Object != null && exp3.Arguments.Count > 0)
                        return ExpressionBinary("=", exp3.Object, exp3.Arguments[0], tsc);
                    if (callType.FullName.StartsWith("FreeSql.ISelectGroupingAggregate`"))
                    {
                        //if (exp3.Type == typeof(string) && exp3.Arguments.Any() && exp3.Arguments[0].NodeType == ExpressionType.Constant) {
                        //	switch (exp3.Method.Name) {
                        //		case "Sum": return $"sum({(exp3.Arguments[0] as ConstantExpression)?.Value})";
                        //		case "Avg": return $"avg({(exp3.Arguments[0] as ConstantExpression)?.Value})";
                        //		case "Max": return $"max({(exp3.Arguments[0] as ConstantExpression)?.Value})";
                        //		case "Min": return $"min({(exp3.Arguments[0] as ConstantExpression)?.Value})";
                        //	}
                        //}
                        switch (exp3.Method.Name)
                        {
                            case "Count": return "count(1)";
                            case "Sum": return $"sum({ExpressionLambdaToSql(exp3.Arguments[0], tsc)})";
                            case "Avg": return $"avg({ExpressionLambdaToSql(exp3.Arguments[0], tsc)})";
                            case "Max": return $"max({ExpressionLambdaToSql(exp3.Arguments[0], tsc)})";
                            case "Min": return $"min({ExpressionLambdaToSql(exp3.Arguments[0], tsc)})";
                        }
                    }
                    if (callType.FullName.StartsWith("FreeSql.ISelect`"))
                    { //子表查询
                        switch (exp3.Method.Name)
                        {
                            case "Any": //exists
                            case "Count":
                            case "Sum":
                            case "Min":
                            case "Max":
                            case "Avg":
                            case "ToList": //where in
                            case "First":
                                var anyArgs = exp3.Arguments;
                                var exp3Stack = new Stack<Expression>();
                                var exp3tmp = exp3.Object;
                                if (exp3.Method.Name == "Any" && exp3tmp != null && anyArgs.Any())
                                    exp3Stack.Push(Expression.Call(exp3tmp, callType.GetMethod("Where", anyArgs.Select(a => a.Type).ToArray()), anyArgs.ToArray()));
                                while (exp3tmp != null)
                                {
                                    exp3Stack.Push(exp3tmp);
                                    switch (exp3tmp.NodeType)
                                    {
                                        case ExpressionType.Call:
                                            var exp3tmpCall = (exp3tmp as MethodCallExpression);
                                            exp3tmp = exp3tmpCall.Object == null ? exp3tmpCall.Arguments.FirstOrDefault() : exp3tmpCall.Object;
                                            continue;
                                        case ExpressionType.MemberAccess: exp3tmp = (exp3tmp as MemberExpression).Expression; continue;
                                    }
                                    break;
                                }
                                object fsql = null;
                                List<SelectTableInfo> fsqltables = null;
                                var fsqltable1SetAlias = false;
                                Type fsqlType = null;
                                Stack<Expression> asSelectBefores = new Stack<Expression>();
                                var asSelectSql = "";
                                Type asSelectEntityType = null;
                                MemberExpression asSelectParentExp1 = null;
                                Expression asSelectParentExp = null;
                                while (exp3Stack.Any())
                                {
                                    exp3tmp = exp3Stack.Pop();
                                    if (exp3tmp.Type.FullName.StartsWith("FreeSql.ISelect`") && fsql == null)
                                    {
                                        if (exp3tmp.NodeType == ExpressionType.Call)
                                        {
                                            var exp3tmpCall = (exp3tmp as MethodCallExpression);
                                            if (exp3tmpCall.Method.Name == "AsSelect" && exp3tmpCall.Object == null)
                                            {
                                                var exp3tmpArg1Type = exp3tmpCall.Arguments.FirstOrDefault()?.Type;
                                                if (exp3tmpArg1Type != null)
                                                {
                                                    asSelectEntityType = exp3tmpArg1Type.GetElementType() ?? exp3tmpArg1Type.GetGenericArguments().FirstOrDefault();
                                                    if (asSelectEntityType != null)
                                                    {
                                                        fsql = _dicExpressionLambdaToSqlAsSelectMethodInfo.GetOrAdd(asSelectEntityType, asSelectEntityType2 => typeof(IFreeSql).GetMethod("Select", new Type[0]).MakeGenericMethod(asSelectEntityType2))
                                                            .Invoke(_common._orm, null);

                                                        if (asSelectBefores.Any())
                                                        {
                                                            asSelectParentExp1 = asSelectBefores.Pop() as MemberExpression;
                                                            if (asSelectBefores.Any())
                                                            {
                                                                asSelectParentExp = asSelectBefores.Pop();
                                                                if (asSelectParentExp != null)
                                                                {
                                                                    var testExecuteExp = asSelectParentExp;
                                                                    if (asSelectParentExp.NodeType == ExpressionType.Parameter) //执行leftjoin关联
                                                                        testExecuteExp = Expression.Property(testExecuteExp, _common.GetTableByEntity(asSelectParentExp.Type).ColumnsByCs.First().Key);
                                                                    var tsc2 = tsc.CloneSetgetSelectGroupingMapStringAndgetSelectGroupingMapStringAndtbtype(new List<SelectColumnInfo>(), tsc.getSelectGroupingMapString, SelectTableInfoType.LeftJoin);
                                                                    tsc2.isDisableDiyParse = true;
                                                                    tsc2.style = ExpressionStyle.AsSelect;
                                                                    asSelectSql = ExpressionLambdaToSql(testExecuteExp, tsc2);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        if (fsql == null) fsql = Expression.Lambda(exp3tmp).Compile().DynamicInvoke();
                                        fsqlType = fsql?.GetType();
                                        if (fsqlType == null) break;
                                        if (exp3.Method.Name != "ToList")
                                            fsqlType.GetField("_limit", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fsql, 1);
                                        fsqltables = fsqlType.GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fsql) as List<SelectTableInfo>;
                                        //fsqltables[0].Alias = $"{tsc._tables[0].Alias}_{fsqltables[0].Alias}";
                                        if (fsqltables != tsc._tables)
                                            fsqltables.AddRange(tsc._tables.Select(a => new SelectTableInfo
                                            {
                                                Alias = a.Alias,
                                                On = "1=1",
                                                Table = a.Table,
                                                Type = SelectTableInfoType.Parent,
                                                Parameter = a.Parameter
                                            }));
                                        if (tsc.whereCascadeExpression?.Any() == true)
                                        {
                                            var fsqlCascade = fsqlType.GetField("_whereCascadeExpression", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fsql) as List<LambdaExpression>;
                                            if (fsqlCascade != tsc.whereCascadeExpression)
                                                fsqlCascade.AddRange(tsc.whereCascadeExpression);
                                        }
                                    }
                                    else if (fsqlType != null)
                                    {
                                        var call3Exp = exp3tmp as MethodCallExpression;
                                        var method = call3Exp.Method;
                                        //var method = fsqlType.GetMethod(call3Exp.Method.Name, call3Exp.Arguments.Select(a => a.Type).ToArray());
                                        //if (call3Exp.Method.ContainsGenericParameters) method.MakeGenericMethod(call3Exp.Method.GetGenericArguments());
                                        var parms = method.GetParameters();
                                        var args = new object[call3Exp.Arguments.Count];
                                        for (var a = 0; a < args.Length; a++)
                                        {
                                            var arg3Exp = call3Exp.Arguments[a];
                                            if (arg3Exp.NodeType == ExpressionType.Constant)
                                            {
                                                args[a] = (arg3Exp as ConstantExpression)?.Value;
                                            }
                                            else
                                            {
                                                var argExp = (arg3Exp as UnaryExpression)?.Operand;
                                                if (argExp != null && argExp.NodeType == ExpressionType.Lambda)
                                                {
                                                    if (fsqltable1SetAlias == false)
                                                    {
                                                        fsqltables[0].Alias = (argExp as LambdaExpression).Parameters.First().Name;
                                                        fsqltable1SetAlias = true;
                                                    }
                                                }
                                                args[a] = argExp ?? Expression.Lambda(arg3Exp).Compile().DynamicInvoke();
                                                //if (args[a] == null) ExpressionLambdaToSql(call3Exp.Arguments[a], fsqltables, null, null, SelectTableInfoType.From, true);
                                            }
                                        }
                                        method.Invoke(fsql, args);
                                    }
                                    if (fsql == null) asSelectBefores.Push(exp3tmp);
                                }
                                if (fsql != null)
                                {
                                    if (asSelectParentExp != null)
                                    { //执行 asSelect() 的关联，OneToMany，ManyToMany
                                        if (fsqltables[0].Parameter == null)
                                        {
                                            fsqltables[0].Alias = $"tb_{fsqltables.Count}";
                                            fsqltables[0].Parameter = Expression.Parameter(asSelectEntityType, fsqltables[0].Alias);
                                        }
                                        var fsqlWhere = _dicExpressionLambdaToSqlAsSelectWhereMethodInfo.GetOrAdd(asSelectEntityType, asSelectEntityType3 =>
                                            typeof(ISelect<>).MakeGenericType(asSelectEntityType3).GetMethod("Where", new[] {
                                            typeof(Expression<>).MakeGenericType(typeof(Func<,>).MakeGenericType(asSelectEntityType3, typeof(bool)))
                                        }));
                                        var parm123Tb = _common.GetTableByEntity(asSelectParentExp.Type);
                                        var parm123Ref = parm123Tb.GetTableRef(asSelectParentExp1.Member.Name, true);
                                        if (parm123Ref != null)
                                        {
                                            var fsqlWhereParam = fsqltables.First().Parameter; //Expression.Parameter(asSelectEntityType);
                                            Expression fsqlWhereExp = null;
                                            if (parm123Ref.RefType == TableRefType.ManyToMany)
                                            {
                                                //g.mysql.Select<Tag>().Where(a => g.mysql.Select<Song_tag>().Where(b => b.Tag_id == a.Id && b.Song_id == 1).Any());
                                                var manyTb = _common.GetTableByEntity(parm123Ref.RefMiddleEntityType);
                                                var manySubSelectWhere = _dicExpressionLambdaToSqlAsSelectWhereMethodInfo.GetOrAdd(parm123Ref.RefMiddleEntityType, refMiddleEntityType3 =>
                                                    typeof(ISelect<>).MakeGenericType(refMiddleEntityType3).GetMethod("Where", new[] {
                                            typeof(Expression<>).MakeGenericType(typeof(Func<,>).MakeGenericType(refMiddleEntityType3, typeof(bool)))
                                                }));
                                                var manySubSelectWhereSql = _dicExpressionLambdaToSqlAsSelectWhereSqlMethodInfo.GetOrAdd(parm123Ref.RefMiddleEntityType, refMiddleEntityType3 =>
                                                    typeof(ISelect0<,>).MakeGenericType(typeof(ISelect<>).MakeGenericType(refMiddleEntityType3), refMiddleEntityType3).GetMethod("Where", new[] { typeof(string), typeof(object) }));
                                                var manySubSelectAny = _dicExpressionLambdaToSqlAsSelectAnyMethodInfo.GetOrAdd(parm123Ref.RefMiddleEntityType, refMiddleEntityType3 =>
                                                    typeof(ISelect0<,>).MakeGenericType(typeof(ISelect<>).MakeGenericType(refMiddleEntityType3), refMiddleEntityType3).GetMethod("Any", new Type[0]));
                                                var manySubSelectAsSelectExp = _dicFreeSqlGlobalExtensionsAsSelectExpression.GetOrAdd(parm123Ref.RefMiddleEntityType, refMiddleEntityType3 =>
                                                    Expression.Call(
                                                        typeof(FreeSqlGlobalExtensions).GetMethods(BindingFlags.Static | BindingFlags.Public).Where(mfil => mfil.Name == "AsSelect" && mfil.GetParameters().Length == 1).FirstOrDefault()?.MakeGenericMethod(refMiddleEntityType3),
                                                        Expression.Constant(Activator.CreateInstance(typeof(List<>).MakeGenericType(refMiddleEntityType3)))
                                                    ));
                                                var manyMainParam = tsc._tables[0].Parameter;
                                                var manySubSelectWhereParam = Expression.Parameter(parm123Ref.RefMiddleEntityType, $"M{fsqlWhereParam.Name}_M{asSelectParentExp.ToString().Replace(".", "__")}");//, $"{fsqlWhereParam.Name}__");
                                                Expression manySubSelectWhereExp = null;
                                                for (var mn = 0; mn < parm123Ref.Columns.Count; mn++)
                                                {
                                                    var col1 = parm123Ref.MiddleColumns[mn];
                                                    var col2 = parm123Ref.Columns[mn];
                                                    var pexp1 = Expression.Property(manySubSelectWhereParam, col1.CsName);
                                                    var pexp2 = Expression.Property(asSelectParentExp, col2.CsName);
                                                    if (col1.CsType != col2.CsType)
                                                    {
                                                        if (col1.CsType.IsNullableType()) pexp1 = Expression.Property(pexp1, _dicNullableValueProperty.GetOrAdd(col1.CsType, ct1 => ct1.GetProperty("Value")));
                                                        if (col2.CsType.IsNullableType()) pexp2 = Expression.Property(pexp2, _dicNullableValueProperty.GetOrAdd(col2.CsType, ct2 => ct2.GetProperty("Value")));
                                                    }
                                                    var tmpExp = Expression.Equal(pexp1, pexp2);
                                                    if (mn == 0) manySubSelectWhereExp = tmpExp;
                                                    else manySubSelectWhereExp = Expression.AndAlso(manySubSelectWhereExp, tmpExp);
                                                }
                                                var manySubSelectExpBoy = Expression.Call(
                                                    manySubSelectAsSelectExp,
                                                    manySubSelectWhere,
                                                    Expression.Lambda(
                                                        manySubSelectWhereExp,
                                                        manySubSelectWhereParam
                                                    )
                                                );
                                                Expression fsqlManyWhereExp = null;
                                                for (var mn = 0; mn < parm123Ref.RefColumns.Count; mn++)
                                                {
                                                    var col1 = parm123Ref.RefColumns[mn];
                                                    var col2 = parm123Ref.MiddleColumns[mn + parm123Ref.Columns.Count + mn];
                                                    var pexp1 = Expression.Property(fsqlWhereParam, col1.CsName);
                                                    var pexp2 = Expression.Property(manySubSelectWhereParam, col2.CsName);
                                                    if (col1.CsType != col2.CsType)
                                                    {
                                                        if (col1.CsType.IsNullableType()) pexp1 = Expression.Property(pexp1, _dicNullableValueProperty.GetOrAdd(col1.CsType, ct1 => ct1.GetProperty("Value")));
                                                        if (col2.CsType.IsNullableType()) pexp2 = Expression.Property(pexp2, _dicNullableValueProperty.GetOrAdd(col2.CsType, ct2 => ct2.GetProperty("Value")));
                                                    }
                                                    var tmpExp = Expression.Equal(pexp1, pexp2);
                                                    if (mn == 0) fsqlManyWhereExp = tmpExp;
                                                    else fsqlManyWhereExp = Expression.AndAlso(fsqlManyWhereExp, tmpExp);
                                                }
                                                fsqltables.Add(new SelectTableInfo { Alias = manySubSelectWhereParam.Name, Parameter = manySubSelectWhereParam, Table = manyTb, Type = SelectTableInfoType.Parent });
                                                fsqlWhere.Invoke(fsql, new object[] { Expression.Lambda(fsqlManyWhereExp, fsqlWhereParam) });
                                                var sql2 = fsqlType.GetMethod("ToSql", new Type[] { typeof(string) })?.Invoke(fsql, new object[] { "1" })?.ToString();
                                                if (string.IsNullOrEmpty(sql2) == false)
                                                    manySubSelectExpBoy = Expression.Call(manySubSelectExpBoy, manySubSelectWhereSql, Expression.Constant($"exists({sql2.Replace("\r\n", "\r\n\t")})"), Expression.Constant(null));
                                                manySubSelectExpBoy = Expression.Call(manySubSelectExpBoy, manySubSelectAny);
                                                asSelectBefores.Clear();

                                                return ExpressionLambdaToSql(manySubSelectExpBoy, tsc);
                                            }
                                            for (var mn = 0; mn < parm123Ref.Columns.Count; mn++)
                                            {
                                                var col1 = parm123Ref.RefColumns[mn];
                                                var col2 = parm123Ref.Columns[mn];
                                                var pexp1 = Expression.Property(fsqlWhereParam, col1.CsName);
                                                var pexp2 = Expression.Property(asSelectParentExp, col2.CsName);
                                                if (col1.CsType != col2.CsType)
                                                {
                                                    if (col1.CsType.IsNullableType()) pexp1 = Expression.Property(pexp1, _dicNullableValueProperty.GetOrAdd(col1.CsType, ct1 => ct1.GetProperty("Value")));
                                                    if (col2.CsType.IsNullableType()) pexp2 = Expression.Property(pexp2, _dicNullableValueProperty.GetOrAdd(col2.CsType, ct2 => ct2.GetProperty("Value")));
                                                }
                                                var tmpExp = Expression.Equal(pexp1, pexp2);
                                                if (mn == 0) fsqlWhereExp = tmpExp;
                                                else fsqlWhereExp = Expression.AndAlso(fsqlWhereExp, tmpExp);
                                            }
                                            fsqlWhere.Invoke(fsql, new object[] { Expression.Lambda(fsqlWhereExp, fsqlWhereParam) });
                                        }
                                    }
                                    asSelectBefores.Clear();

                                    switch (exp3.Method.Name)
                                    {
                                        case "Any":
                                            var sql = fsqlType.GetMethod("ToSql", new Type[] { typeof(string) })?.Invoke(fsql, new object[] { "1" })?.ToString();
                                            if (string.IsNullOrEmpty(sql) == false)
                                                return $"exists({sql.Replace("\r\n", "\r\n\t")})";
                                            break;
                                        case "Count":
                                            var sqlCount = fsqlType.GetMethod("ToSql", new Type[] { typeof(string) })?.Invoke(fsql, new object[] { "count(1)" })?.ToString();
                                            if (string.IsNullOrEmpty(sqlCount) == false)
                                                return $"({sqlCount.Replace("\r\n", "\r\n\t")})";
                                            break;
                                        case "Sum":
                                        case "Min":
                                        case "Max":
                                        case "Avg":
                                            var tscClone1 = tsc.CloneDisableDiyParse();
                                            tscClone1.isDisableDiyParse = false;
                                            tscClone1._tables = fsqltables;
                                            var sqlSum = fsqlType.GetMethod("ToSql", new Type[] { typeof(string) })?.Invoke(fsql, new object[] { $"{exp3.Method.Name.ToLower()}({ExpressionLambdaToSql(exp3.Arguments.FirstOrDefault(), tscClone1)})" })?.ToString();
                                            if (string.IsNullOrEmpty(sqlSum) == false)
                                                return $"({sqlSum.Replace("\r\n", "\r\n\t")})";
                                            break;
                                        case "ToList":
                                        case "First":
                                            var tscClone2 = tsc.CloneDisableDiyParse();
                                            tscClone2.isDisableDiyParse = false;
                                            tscClone2._tables = fsqltables;
                                            var sqlFirst = fsqlType.GetMethod("ToSql", new Type[] { typeof(string) })?.Invoke(fsql, new object[] { ExpressionLambdaToSql(exp3.Arguments.FirstOrDefault(), tscClone2) })?.ToString();
                                            if (string.IsNullOrEmpty(sqlFirst) == false)
                                                return $"({sqlFirst.Replace("\r\n", "\r\n\t")})";
                                            break;
                                    }
                                }
                                asSelectBefores.Clear();
                                break;
                        }
                    }
                    //var eleType = callType.GetElementType() ?? callType.GenericTypeArguments.FirstOrDefault();
                    //if (eleType != null && typeof(IEnumerable<>).MakeGenericType(eleType).IsAssignableFrom(callType)) { //集合导航属性子查询
                    //	if (exp3.Method.Name == "Any") { //exists

                    //	}
                    //}
                    other3Exp = ExpressionLambdaToSqlOther(exp3, tsc);
                    if (string.IsNullOrEmpty(other3Exp) == false) return other3Exp;
                    if (exp3.IsParameter() == false) return formatSql(Expression.Lambda(exp3).Compile().DynamicInvoke(), tsc.mapType, tsc.mapColumnTmp, tsc.dbParams);
                    throw new Exception($"未实现函数表达式 {exp3} 解析");
                case ExpressionType.Parameter:
                case ExpressionType.MemberAccess:
                    var exp4 = exp as MemberExpression;
                    if (exp4 != null)
                    {
                        if (exp4.Expression != null && exp4.Expression.Type.IsArray == false && exp4.Expression.Type.IsNullableType())
                            return exp4.Member.Name == "HasValue" ? $"{ExpressionLambdaToSql(exp4.Expression, tsc)} IS NOT NULL" : ExpressionLambdaToSql(exp4.Expression, tsc);
                        var extRet = "";
                        var memberType = exp4.Expression?.Type ?? exp4.Type;
                        switch (memberType.FullName)
                        {
                            case "System.String": extRet = ExpressionLambdaToSqlMemberAccessString(exp4, tsc); break;
                            case "System.DateTime": extRet = ExpressionLambdaToSqlMemberAccessDateTime(exp4, tsc); break;
                            case "System.TimeSpan": extRet = ExpressionLambdaToSqlMemberAccessTimeSpan(exp4, tsc); break;
                        }
                        if (string.IsNullOrEmpty(extRet) == false) return extRet;
                        var other4Exp = ExpressionLambdaToSqlOther(exp4, tsc);
                        if (string.IsNullOrEmpty(other4Exp) == false) return other4Exp;
                    }
                    var expStack = new Stack<Expression>();
                    var expStackConstOrMemberCount = 1;
                    expStack.Push(exp);
                    MethodCallExpression callExp = null;
                    var exp2 = exp4?.Expression;
                    while (true)
                    {
                        switch (exp2?.NodeType)
                        {
                            case ExpressionType.Constant:
                                expStack.Push(exp2);
                                expStackConstOrMemberCount++;
                                break;
                            case ExpressionType.Parameter:
                                expStack.Push(exp2);
                                break;
                            case ExpressionType.MemberAccess:
                                expStack.Push(exp2);
                                exp2 = (exp2 as MemberExpression).Expression;
                                expStackConstOrMemberCount++;
                                if (exp2 == null) break;
                                continue;
                            case ExpressionType.Call:
                                callExp = exp2 as MethodCallExpression;
                                expStack.Push(exp2);
                                exp2 = callExp.Object;
                                if (exp2 == null) break;
                                continue;
                            case ExpressionType.TypeAs:
                            case ExpressionType.Convert:
                                var oper2 = (exp2 as UnaryExpression).Operand;
                                if (oper2.NodeType == ExpressionType.Parameter)
                                {
                                    var oper2Parm = oper2 as ParameterExpression;
                                    expStack.Push(exp2.Type.IsAbstract || exp2.Type.IsInterface ? oper2Parm : Expression.Parameter(exp2.Type, oper2Parm.Name));
                                }
                                else
                                    expStack.Push(oper2);
                                break;
                        }
                        break;
                    }
                    if (expStack.First().NodeType != ExpressionType.Parameter)
                    {
                        if (expStackConstOrMemberCount == expStack.Count)
                        {
                            object firstValue = null;
                            switch (expStack.First().NodeType)
                            {
                                case ExpressionType.Constant:
                                    var expStackFirst = expStack.Pop() as ConstantExpression;
                                    firstValue = expStackFirst?.Value;
                                    break;
                                case ExpressionType.MemberAccess:
                                    var expStackFirstMem = expStack.First() as MemberExpression;
                                    if (expStackFirstMem.Expression?.NodeType == ExpressionType.Constant) firstValue = (expStackFirstMem.Expression as ConstantExpression)?.Value;
                                    break;
                            }
                            while (expStack.Any())
                            {
                                var expStackItem = expStack.Pop() as MemberExpression;
                                if (expStackItem.Member.MemberType == MemberTypes.Property)
                                    firstValue = ((PropertyInfo)expStackItem.Member).GetValue(firstValue, null);
                                else if (expStackItem.Member.MemberType == MemberTypes.Field)
                                    firstValue = ((FieldInfo)expStackItem.Member).GetValue(firstValue);
                            }
                            return formatSql(firstValue, tsc.mapType, tsc.mapColumnTmp, tsc.dbParams);
                        }
                        return formatSql(Expression.Lambda(exp).Compile().DynamicInvoke(), tsc.mapType, tsc.mapColumnTmp, tsc.dbParams);
                    }
                    if (callExp != null) return ExpressionLambdaToSql(callExp, tsc);
                    if (tsc.getSelectGroupingMapString != null && expStack.First().Type.FullName.StartsWith("FreeSql.ISelectGroupingAggregate`"))
                    {
                        if (tsc.getSelectGroupingMapString != null)
                        {
                            var expText = tsc.getSelectGroupingMapString(expStack.Where((a, b) => b >= 2).ToArray());
                            if (string.IsNullOrEmpty(expText) == false) return expText;
                        }
                    }

                    if (tsc._tables == null)
                    {
                        var pp = expStack.Pop() as ParameterExpression;
                        var memberExp = expStack.Pop() as MemberExpression;
                        var tb = _common.GetTableByEntity(pp.Type);
                        if (tb.ColumnsByCs.ContainsKey(memberExp.Member.Name) == false)
                        {
                            if (tb.ColumnsByCsIgnore.ContainsKey(memberExp.Member.Name))
                                throw new ArgumentException($"{tb.DbName}.{memberExp.Member.Name} 被忽略，请检查 IsIgnore 设置，确认 get/set 为 public");
                            throw new ArgumentException($"{tb.DbName} 找不到列 {memberExp.Member.Name}");
                        }
                        var curcol = tb.ColumnsByCs[memberExp.Member.Name];
                        if (tsc._selectColumnMap != null)
                            tsc._selectColumnMap.Add(new SelectColumnInfo { Table = null, Column = curcol });
                        var name = curcol.Attribute.Name;
                        if (tsc.isQuoteName) name = _common.QuoteSqlName(name);
                        tsc.SetMapColumnTmp(curcol);
                        if (string.IsNullOrEmpty(tsc.alias001)) return name;
                        return $"{tsc.alias001}.{name}";
                    }
                    Func<TableInfo, string, bool, ParameterExpression, MemberExpression, SelectTableInfo> getOrAddTable = (tbtmp, alias, isa, parmExp, mp) =>
                    {
                        var finds = new SelectTableInfo[0];
                        if (tsc.style == ExpressionStyle.SelectColumns)
                        {
                            finds = tsc._tables.Where(a => a.Table.Type == tbtmp.Type && a.Alias == alias).ToArray();
                            if (finds.Length != 1) finds = tsc._tables.Where(a => a.Table.Type == tbtmp.Type).ToArray();
                            if (finds.Any()) finds = new[] { finds.First() };
                        }
                        if (finds.Length != 1 && isa && parmExp != null)
                            finds = tsc._tables.Where(a => a.Parameter == parmExp).ToArray();
                        if (finds.Length != 1)
                        {
                            var navdot = string.IsNullOrEmpty(alias) ? new SelectTableInfo[0] : tsc._tables.Where(a2 => a2.Parameter != null && alias.StartsWith($"{a2.Alias}__")).ToArray();
                            if (navdot.Length > 0)
                            {
                                var isthis = navdot[0] == tsc._tables[0];
                                finds = tsc._tables.Where(a2 => (isa && a2.Parameter != null || !isa && a2.Parameter == null) &&
                                    a2.Table.Type == tbtmp.Type && a2.Alias == alias && a2.Alias.StartsWith($"{navdot[0].Alias}__") &&
                                    (isthis && a2.Type != SelectTableInfoType.Parent || !isthis && a2.Type == SelectTableInfoType.Parent)).ToArray();
                                if (finds.Length == 0)
                                    finds = tsc._tables.Where(a2 =>
                                         a2.Table.Type == tbtmp.Type && a2.Alias == alias && a2.Alias.StartsWith($"{navdot[0].Alias}__") &&
                                         (isthis && a2.Type != SelectTableInfoType.Parent || !isthis && a2.Type == SelectTableInfoType.Parent)).ToArray();
                            }
                            else
                            {
                                finds = tsc._tables.Where(a2 => (isa && a2.Parameter != null || isa && a2.Parameter == null) &&
                                    a2.Table.Type == tbtmp.Type && a2.Alias == alias).ToArray();
                                if (finds.Length != 1)
                                {
                                    finds = tsc._tables.Where(a2 => (isa && a2.Parameter != null || isa && a2.Parameter == null) &&
                                        a2.Table.Type == tbtmp.Type).ToArray();
                                    if (finds.Length != 1)
                                    {
                                        finds = tsc._tables.Where(a2 => (isa && a2.Parameter != null || isa && a2.Parameter == null) &&
                                            a2.Table.Type == tbtmp.Type).ToArray();
                                        if (finds.Length != 1)
                                            finds = tsc._tables.Where(a2 => a2.Table.Type == tbtmp.Type).ToArray();
                                    }
                                }
                            }
                            //finds = tsc._tables.Where((a2, c2) => (isa || a2.Parameter == null) && a2.Table.CsName == tbtmp.CsName && (isthis && a2.Type != SelectTableInfoType.Parent || !isthis)).ToArray(); //外部表，内部表一起查
                            //if (finds.Length > 1) {
                            //	finds = tsc._tables.Where((a2, c2) => (isa || a2.Parameter == null) && a2.Table.CsName == tbtmp.CsName && a2.Type == SelectTableInfoType.Parent && a2.Alias == alias).ToArray(); //查询外部表
                            //	if (finds.Any() == false) {
                            //		finds = tsc._tables.Where((a2, c2) => (isa || a2.Parameter == null) && a2.Table.CsName == tbtmp.CsName && a2.Type != SelectTableInfoType.Parent).ToArray(); //查询内部表
                            //		if (finds.Length > 1)
                            //			finds = tsc._tables.Where((a2, c2) => (isa || a2.Parameter == null) && a2.Table.CsName == tbtmp.CsName && a2.Type != SelectTableInfoType.Parent && a2.Alias == alias).ToArray();
                            //	}
                            //}
                        }
                        var find = finds.Length == 1 ? finds.First() : null;
                        if (find != null && isa && parmExp != null && find.Parameter != parmExp)
                            find.Parameter = parmExp;
                        if (find == null)
                        {
                            tsc._tables.Add(find = new SelectTableInfo { Table = tbtmp, Alias = alias, On = null, Type = mp == null ? tsc.tbtype : SelectTableInfoType.LeftJoin, Parameter = isa ? parmExp : null });
                            if (mp?.Expression != null)
                            { //导航条件，OneToOne、ManyToOne
                                var firstTb = tsc._tables.First().Table;
                                var parentTb = _common.GetTableByEntity(mp.Expression.Type);
                                var parentTbRef = parentTb?.GetTableRef(mp.Member.Name, tsc.style == ExpressionStyle.AsSelect);
                                if (parentTbRef != null)
                                {
                                    Expression navCondExp = null;
                                    for (var mn = 0; mn < parentTbRef.Columns.Count; mn++)
                                    {
                                        var col1 = parentTbRef.RefColumns[mn];
                                        var col2 = parentTbRef.Columns[mn];
                                        var pexp1 = Expression.Property(mp, col1.CsName);
                                        var pexp2 = Expression.Property(mp.Expression, col2.CsName);
                                        if (col1.CsType != col2.CsType)
                                        {
                                            if (col1.CsType.IsNullableType()) pexp1 = Expression.Property(pexp1, _dicNullableValueProperty.GetOrAdd(col1.CsType, ct1 => ct1.GetProperty("Value")));
                                            if (col2.CsType.IsNullableType()) pexp2 = Expression.Property(pexp2, _dicNullableValueProperty.GetOrAdd(col2.CsType, ct2 => ct2.GetProperty("Value")));
                                        }
                                        var tmpExp = Expression.Equal(pexp1, pexp2);
                                        if (mn == 0) navCondExp = tmpExp;
                                        else navCondExp = Expression.AndAlso(navCondExp, tmpExp);
                                    }
                                    if (find.Type == SelectTableInfoType.InnerJoin ||
                                        find.Type == SelectTableInfoType.LeftJoin ||
                                        find.Type == SelectTableInfoType.RightJoin)
                                        find.On = ExpressionLambdaToSql(navCondExp, tsc.CloneSetgetSelectGroupingMapStringAndgetSelectGroupingMapStringAndtbtype(null, null, find.Type));
                                    else
                                        find.NavigateCondition = ExpressionLambdaToSql(navCondExp, tsc.CloneSetgetSelectGroupingMapStringAndgetSelectGroupingMapStringAndtbtype(null, null, find.Type));
                                }
                            }
                        }
                        return find;
                    };

                    TableInfo tb2 = null;
                    ParameterExpression parmExp2 = null;
                    string alias2 = "", name2 = "";
                    SelectTableInfo find2 = null;
                    while (expStack.Count > 0)
                    {
                        exp2 = expStack.Pop();
                        switch (exp2.NodeType)
                        {
                            case ExpressionType.Constant:
                                throw new NotImplementedException("未实现 MemberAccess 下的 Constant");
                            case ExpressionType.Parameter:
                            case ExpressionType.MemberAccess:

                                var exp2Type = exp2.Type;
                                if (exp2Type.FullName.StartsWith("FreeSql.ISelectGroupingAggregate`")) exp2Type = exp2Type.GetGenericArguments().LastOrDefault() ?? exp2.Type;
                                var tb2tmp = _common.GetTableByEntity(exp2Type);
                                var mp2 = exp2 as MemberExpression;
                                if (mp2?.Member.Name == "Key" && mp2.Expression.Type.FullName.StartsWith("FreeSql.ISelectGroupingAggregate`")) continue;
                                if (tb2tmp != null)
                                {
                                    if (exp2.NodeType == ExpressionType.Parameter)
                                    {
                                        parmExp2 = (exp2 as ParameterExpression);
                                        alias2 = parmExp2.Name;
                                    }
                                    else alias2 = $"{alias2}__{mp2.Member.Name}";
                                    find2 = getOrAddTable(tb2tmp, alias2, exp2.NodeType == ExpressionType.Parameter, parmExp2, mp2);
                                    alias2 = find2.Alias;
                                    tb2 = tb2tmp;
                                }
                                if (exp2.NodeType == ExpressionType.Parameter && expStack.Any() == false)
                                { //附加选择的参数所有列
                                    if (tsc._selectColumnMap != null)
                                    {
                                        foreach (var tb2c in tb2.Columns.Values)
                                            tsc._selectColumnMap.Add(new SelectColumnInfo { Table = find2, Column = tb2c });
                                        if (tb2.Columns.Any()) return "";
                                    }
                                }
                                if (mp2 == null || expStack.Any()) continue;
                                if (tb2.ColumnsByCs.ContainsKey(mp2.Member.Name) == false)
                                { //如果选的是对象，附加所有列
                                    if (tsc._selectColumnMap != null)
                                    {
                                        var tb3 = _common.GetTableByEntity(mp2.Type);
                                        if (tb3 != null)
                                        {
                                            var find3 = getOrAddTable(tb2tmp, alias2 /*$"{alias2}__{mp2.Member.Name}"*/, exp2.NodeType == ExpressionType.Parameter, parmExp2, mp2);

                                            foreach (var tb3c in tb3.Columns.Values)
                                                tsc._selectColumnMap.Add(new SelectColumnInfo { Table = find3, Column = tb3c });
                                            if (tb3.Columns.Any()) return "";
                                        }
                                    }
                                    if (tb2.ColumnsByCsIgnore.ContainsKey(mp2.Member.Name))
                                        throw new ArgumentException($"{tb2.DbName}.{mp2.Member.Name} 被忽略，请检查 IsIgnore 设置，确认 get/set 为 public");
                                    throw new ArgumentException($"{tb2.DbName} 找不到列 {mp2.Member.Name}");
                                }
                                var col2 = tb2.ColumnsByCs[mp2.Member.Name];
                                if (tsc._selectColumnMap != null && find2 != null)
                                {
                                    tsc._selectColumnMap.Add(new SelectColumnInfo { Table = find2, Column = col2 });
                                    return "";
                                }
                                name2 = col2.Attribute.Name;
                                tsc.SetMapColumnTmp(col2);
                                break;
                            case ExpressionType.Call: break;
                        }
                    }
                    if (tsc.isQuoteName) name2 = _common.QuoteSqlName(name2);
                    return $"{alias2}.{name2}";
            }
            var expBinary = exp as BinaryExpression;
            if (expBinary == null)
            {
                var other99Exp = ExpressionLambdaToSqlOther(exp, tsc);
                if (string.IsNullOrEmpty(other99Exp) == false) return other99Exp;
                return "";
            }
            switch (expBinary.NodeType)
            {
                case ExpressionType.Coalesce:
                    return _common.IsNull(ExpressionLambdaToSql(expBinary.Left, tsc), ExpressionLambdaToSql(expBinary.Right, tsc));
            }
            if (dicExpressionOperator.TryGetValue(expBinary.NodeType, out var tryoper) == false)
            {
                if (exp.IsParameter() == false) return formatSql(Expression.Lambda(exp).Compile().DynamicInvoke(), tsc.mapType, tsc.mapColumnTmp, tsc.dbParams);
                return "";
            }
            return ExpressionBinary(tryoper, expBinary.Left, expBinary.Right, tsc);
        }

        public abstract string ExpressionLambdaToSqlMemberAccessString(MemberExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlMemberAccessDateTime(MemberExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlMemberAccessTimeSpan(MemberExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlCallString(MethodCallExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlCallMath(MethodCallExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlCallDateTime(MethodCallExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlCallTimeSpan(MethodCallExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlCallConvert(MethodCallExpression exp, ExpTSC tsc);
        public abstract string ExpressionLambdaToSqlOther(Expression exp, ExpTSC tsc);

        public enum ExpressionStyle
        {
            Where, AsSelect, SelectColumns
        }
        public class ExpTSC
        {
            public List<SelectTableInfo> _tables { get; set; }
            public List<SelectColumnInfo> _selectColumnMap { get; set; }
            public Func<Expression[], string> getSelectGroupingMapString { get; set; }
            public SelectTableInfoType tbtype { get; set; }
            public bool isQuoteName { get; set; }
            public bool isDisableDiyParse { get; set; }
            public ExpressionStyle style { get; set; }
            public Type mapType { get; set; }
            public Type mapTypeTmp { get; set; }
            public ColumnInfo mapColumnTmp { get; set; }
            public TableInfo currentTable { get; set; }
            public List<LambdaExpression> whereCascadeExpression { get; set; }
            public List<DbParameter> dbParams { get; set; }
            public string alias001 { get; set; } //单表字段的表别名

            public ExpTSC SetMapColumnTmp(ColumnInfo col)
            {
                if (col == null)
                {
                    this.mapTypeTmp = null;
                    this.mapColumnTmp = null;
                }
                else
                {
                    this.mapTypeTmp = col.Attribute.MapType == col.CsType ? null : col.Attribute.MapType;
                    this.mapColumnTmp = col;
                }
                return this;
            }
            public Type SetMapTypeReturnOld(Type newValue)
            {
                var old = this.mapType;
                this.mapType = newValue;
                return old;
            }
            public List<DbParameter> SetDbParamsReturnOld(List<DbParameter> newValue)
            {
                var old = this.dbParams;
                this.dbParams = newValue;
                return old;
            }

            public ExpTSC CloneSetgetSelectGroupingMapStringAndgetSelectGroupingMapStringAndtbtype(List<SelectColumnInfo> v1, Func<Expression[], string> v2, SelectTableInfoType v3)
            {
                return new ExpTSC
                {
                    _tables = this._tables,
                    _selectColumnMap = v1,
                    getSelectGroupingMapString = v2,
                    tbtype = v3,
                    isQuoteName = this.isQuoteName,
                    isDisableDiyParse = this.isDisableDiyParse,
                    style = this.style,
                    //mapType = this.mapType,
                    //mapTypeTmp = this.mapTypeTmp,
                    //mapColumnTmp = this.mapColumnTmp,
                    currentTable = this.currentTable,
                    whereCascadeExpression = this.whereCascadeExpression,
                    dbParams = this.dbParams,
                    alias001 = this.alias001
                };
            }
            public ExpTSC CloneDisableDiyParse()
            {
                return new ExpTSC
                {
                    _tables = this._tables,
                    _selectColumnMap = this._selectColumnMap,
                    getSelectGroupingMapString = this.getSelectGroupingMapString,
                    tbtype = this.tbtype,
                    isQuoteName = this.isQuoteName,
                    isDisableDiyParse = true,
                    style = this.style,
                    mapType = this.mapType,
                    mapTypeTmp = this.mapTypeTmp,
                    mapColumnTmp = this.mapColumnTmp,
                    currentTable = this.currentTable,
                    whereCascadeExpression = this.whereCascadeExpression,
                    dbParams = this.dbParams,
                    alias001 = this.alias001
                };
            }
        }

        static ConcurrentDictionary<string, bool> _dicGetWhereCascadeSqlError = new ConcurrentDictionary<string, bool>();
        public string GetWhereCascadeSql(SelectTableInfo tb, List<LambdaExpression> _whereCascadeExpression)
        {
            if (_whereCascadeExpression.Any())
            {
                var newParameter = Expression.Parameter(tb.Table.Type, "c");
                var sb = new StringBuilder();
                var isEmpty = true;

                foreach (var fl in _whereCascadeExpression)
                {
                    var errorKey = FreeUtil.Sha1($"{tb.Table.Type.FullName},{fl.ToString()}");
                    if (_dicGetWhereCascadeSqlError.ContainsKey(errorKey)) continue;

                    var visitor = new ReplaceVisitor();
                    try
                    {
                        var expExp = Expression.Lambda(
                            typeof(Func<,>).MakeGenericType(tb.Table.Type, typeof(bool)),
                            new ReplaceVisitor().Modify(fl.Body, newParameter),
                            newParameter
                        );
                        var whereSql = ExpressionLambdaToSql(expExp.Body, new ExpTSC { _tables = null, _selectColumnMap = null, getSelectGroupingMapString = null, tbtype = SelectTableInfoType.From, isQuoteName = true, isDisableDiyParse = false, style = ExpressionStyle.Where, currentTable = tb.Table, alias001 = tb.Alias });
                        whereSql = GetBoolString(expExp.Body, whereSql);
                        if (isEmpty == false)
                            sb.Append(" AND ");
                        else
                            isEmpty = false;
                        sb.Append("(").Append(whereSql).Append(")");
                    }
                    catch
                    {
                        _dicGetWhereCascadeSqlError.TryAdd(errorKey, true);
                        continue;
                    }
                }

                if (isEmpty == false)
                    return sb.ToString();
            }
            return null;
        }
        internal class ReplaceVisitor : ExpressionVisitor
        {
            private ParameterExpression parameter;
            public Expression Modify(Expression expression, ParameterExpression parameter)
            {
                this.parameter = parameter;
                return Visit(expression);
            }
            protected override Expression VisitMember(MemberExpression node)
            {
                if (node.Expression?.NodeType == ExpressionType.Parameter)
                    return Expression.Property(parameter, node.Member.Name);
                return base.VisitMember(node);
            }
        }

        public string formatSql(object obj, Type mapType, ColumnInfo mapColumn, List<DbParameter> dbParams)
        {
            //参数化设置，日后优化
            if (_common.CodeFirst.IsGenerateCommandParameterWithLambda && dbParams != null)
            {
                if (obj == null) return "NULL";
                var paramName = $"exp_{dbParams.Count}";
                var parm = _common.AppendParamter(dbParams, paramName, mapColumn,
                    mapType ?? mapColumn?.Attribute.MapType ?? obj?.GetType(), mapType == null ? obj : Utils.GetDataReaderValue(mapType, obj));
                return _common.QuoteParamterName(paramName);
            }
            return string.Concat(_ado.AddslashesProcessParam(obj, mapType, mapColumn));
        }
    }
}
