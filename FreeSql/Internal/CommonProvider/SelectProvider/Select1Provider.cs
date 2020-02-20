﻿using FreeSql.Extensions.EntityUtil;
using FreeSql.Internal.Model;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FreeSql.Internal.CommonProvider
{

    public abstract class Select1Provider<T1> : Select0Provider<ISelect<T1>, T1>, ISelect<T1>
            where T1 : class
    {
        public Select1Provider(IFreeSql orm, CommonUtils commonUtils, CommonExpression commonExpression, object dywhere) : base(orm, commonUtils, commonExpression, dywhere)
        {
            _whereGlobalFilter = _orm.GlobalFilter.GetFilters();
            _whereCascadeExpression.AddRange(_whereGlobalFilter.Select(a => a.Where));
        }

        protected ISelect<T1> InternalFrom(LambdaExpression lambdaExp)
        {
            if (lambdaExp != null)
            {
                for (var a = 1; a < lambdaExp.Parameters.Count; a++)
                {
                    var tb = _commonUtils.GetTableByEntity(lambdaExp.Parameters[a].Type);
                    if (tb == null) throw new ArgumentException($"{lambdaExp.Parameters[a].Name} 类型错误");
                    _tables.Add(new SelectTableInfo { Table = tb, Alias = lambdaExp.Parameters[a].Name, On = null, Type = SelectTableInfoType.From });
                }
            }
            var exp = lambdaExp?.Body;
            if (exp?.NodeType == ExpressionType.Call)
            {
                var expCall = exp as MethodCallExpression;
                var stockCall = new Stack<MethodCallExpression>();
                while (expCall != null)
                {
                    stockCall.Push(expCall);
                    expCall = expCall.Object as MethodCallExpression;
                }
                while (stockCall.Any())
                {
                    expCall = stockCall.Pop();

                    switch (expCall.Method.Name)
                    {
                        case "Where": this.InternalWhere(expCall.Arguments[0]); break;
                        case "WhereIf":
                            var whereIfCond = _commonExpression.ExpressionSelectColumn_MemberAccess(null, null, SelectTableInfoType.From, expCall.Arguments[0], false, null);
                            if (whereIfCond == "1" || whereIfCond == "'t'" || whereIfCond == "-1") //MsAccess -1
                                this.InternalWhere(expCall.Arguments[1]);
                            break;
                        case "OrderBy":
                            if (expCall.Arguments.Count == 2 && expCall.Arguments[0].Type == typeof(bool))
                            {
                                var ifcond = _commonExpression.ExpressionSelectColumn_MemberAccess(null, null, SelectTableInfoType.From, expCall.Arguments[0], false, null);
                                if (ifcond == "1" || ifcond == "'t'")
                                    this.InternalOrderBy(expCall.Arguments.LastOrDefault());
                                break;
                            }
                            this.InternalOrderBy(expCall.Arguments.LastOrDefault());
                            break;
                        case "OrderByDescending":
                            if (expCall.Arguments.Count == 2 && expCall.Arguments[0].Type == typeof(bool))
                            {
                                var ifcond = _commonExpression.ExpressionSelectColumn_MemberAccess(null, null, SelectTableInfoType.From, expCall.Arguments[0], false, null);
                                if (ifcond == "1" || ifcond == "'t'" || ifcond == "-1")//MsAccess -1
                                    this.InternalOrderByDescending(expCall.Arguments.LastOrDefault());
                                break;
                            }
                            this.InternalOrderByDescending(expCall.Arguments.LastOrDefault());
                            break;

                        case "LeftJoin": this.InternalJoin(expCall.Arguments[0], SelectTableInfoType.LeftJoin); break;
                        case "InnerJoin": this.InternalJoin(expCall.Arguments[0], SelectTableInfoType.InnerJoin); break;
                        case "RightJoin": this.InternalJoin(expCall.Arguments[0], SelectTableInfoType.RightJoin); break;

                        default: throw new NotImplementedException($"未实现 {expCall.Method.Name}");
                    }
                }
            }
            return this;
        }

        public ISelect<T1> As(string alias)
        {
            var oldAs = _tables.First().Alias;
            var newAs = string.IsNullOrEmpty(alias) ? "a" : alias;
            if (oldAs != newAs)
            {
                _tables.First().Alias = newAs;
                var wh = _where.ToString();
                _where.Replace($" {oldAs}.", $" {newAs}.");
            }
            return this;
        }

        public double Avg<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return default(double);
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalAvg(column?.Body);
        }

        public abstract ISelect<T1, T2> From<T2>(Expression<Func<ISelectFromExpression<T1>, T2, ISelectFromExpression<T1>>> exp) where T2 : class;// { this.InternalFrom(exp); var ret = new Select3Provider<T1, T2, T3>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3> From<T2, T3>(Expression<Func<ISelectFromExpression<T1>, T2, T3, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class;// { this.InternalFrom(exp); var ret = new Select3Provider<T1, T2, T3>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3, T4> From<T2, T3, T4>(Expression<Func<ISelectFromExpression<T1>, T2, T3, T4, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class where T4 : class;// { this.InternalFrom(exp); var ret = new Select4Provider<T1, T2, T3, T4>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3, T4, T5> From<T2, T3, T4, T5>(Expression<Func<ISelectFromExpression<T1>, T2, T3, T4, T5, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class where T4 : class where T5 : class;// { this.InternalFrom(exp); var ret = new Select5Provider<T1, T2, T3, T4, T5>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3, T4, T5, T6> From<T2, T3, T4, T5, T6>(Expression<Func<ISelectFromExpression<T1>, T2, T3, T4, T5, T6, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class where T4 : class where T5 : class where T6 : class;// { this.InternalFrom(exp); var ret = new Select6Provider<T1, T2, T3, T4, T5, T6>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3, T4, T5, T6, T7> From<T2, T3, T4, T5, T6, T7>(Expression<Func<ISelectFromExpression<T1>, T2, T3, T4, T5, T6, T7, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class where T4 : class where T5 : class where T6 : class where T7 : class;// { this.InternalFrom(exp); var ret = new Select7Provider<T1, T2, T3, T4, T5, T6, T7>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3, T4, T5, T6, T7, T8> From<T2, T3, T4, T5, T6, T7, T8>(Expression<Func<ISelectFromExpression<T1>, T2, T3, T4, T5, T6, T7, T8, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class where T4 : class where T5 : class where T6 : class where T7 : class where T8 : class;// { this.InternalFrom(exp); var ret = new Select8Provider<T1, T2, T3, T4, T5, T6, T7, T8>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3, T4, T5, T6, T7, T8, T9> From<T2, T3, T4, T5, T6, T7, T8, T9>(Expression<Func<ISelectFromExpression<T1>, T2, T3, T4, T5, T6, T7, T8, T9, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class where T4 : class where T5 : class where T6 : class where T7 : class where T8 : class where T9 : class;// { this.InternalFrom(exp); var ret = new Select9Provider<T1, T2, T3, T4, T5, T6, T7, T8, T9>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }
        public abstract ISelect<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> From<T2, T3, T4, T5, T6, T7, T8, T9, T10>(Expression<Func<ISelectFromExpression<T1>, T2, T3, T4, T5, T6, T7, T8, T9, T10, ISelectFromExpression<T1>>> exp) where T2 : class where T3 : class where T4 : class where T5 : class where T6 : class where T7 : class where T8 : class where T9 : class where T10 : class;// { this.InternalFrom(exp); var ret = new Select10Provider<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(_orm, _commonUtils, _commonExpression, null); Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, exp?.Parameters); return ret; }

        public ISelectGrouping<TKey, T1> GroupBy<TKey>(Expression<Func<T1, TKey>> columns)
        {
            if (columns == null) return this.InternalGroupBy<TKey, T1>(columns);
            _tables[0].Parameter = columns.Parameters[0];
            return this.InternalGroupBy<TKey, T1>(columns);
        }

        public TMember Max<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return default(TMember);
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalMax<TMember>(column?.Body);
        }
        public TMember Min<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return default(TMember);
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalMin<TMember>(column?.Body);
        }
        public ISelect<T1> OrderBy<TMember>(Expression<Func<T1, TMember>> column) => this.OrderBy(true, column);
        public ISelect<T1> OrderBy<TMember>(bool condition, Expression<Func<T1, TMember>> column)
        {
            if (condition == false || column == null) return this;
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalOrderBy(column?.Body);
        }
        public ISelect<T1> OrderByDescending<TMember>(Expression<Func<T1, TMember>> column) => this.OrderByDescending(true, column);
        public ISelect<T1> OrderByDescending<TMember>(bool condition, Expression<Func<T1, TMember>> column)
        {
            if (condition == false || column == null) return this;
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalOrderByDescending(column?.Body);
        }

        public decimal Sum<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return default(decimal);
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalSum(column?.Body);
        }

        public List<TReturn> ToList<TReturn>(Expression<Func<T1, TReturn>> select)
        {
            if (select == null) return this.InternalToList<TReturn>(select?.Body);
            _tables[0].Parameter = select.Parameters[0];
            return this.InternalToList<TReturn>(select?.Body);
        }
        
        public List<TDto> ToList<TDto>() => ToList(GetToListDtoSelector<TDto>());
        Expression<Func<T1, TDto>> GetToListDtoSelector<TDto>()
        {
            return Expression.Lambda<Func<T1, TDto>>(
                typeof(TDto).InternalNewExpression(),
                _tables[0].Parameter ?? Expression.Parameter(typeof(T1), "a"));
        }

        #region linq to sql
        public ISelect<TReturn> Select<TReturn>(Expression<Func<T1, TReturn>> select) where TReturn : class
        {
            if (typeof(TReturn) == typeof(T1)) return this as ISelect<TReturn>;
            _tables[0].Parameter = select.Parameters[0];
            _selectExpression = select.Body;
            (_orm.CodeFirst as CodeFirstProvider)._dicSycedTryAdd(typeof(TReturn)); //._dicSyced.TryAdd(typeof(TReturn), true);
            var ret = _orm.Select<TReturn>();
            Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, null);
            return ret;
        }
        public ISelect<TResult> Join<TInner, TKey, TResult>(ISelect<TInner> inner, Expression<Func<T1, TKey>> outerKeySelector, Expression<Func<TInner, TKey>> innerKeySelector, Expression<Func<T1, TInner, TResult>> resultSelector) where TInner : class where TResult : class
        {
            _tables[0].Parameter = resultSelector.Parameters[0];
            _commonExpression.ExpressionLambdaToSql(outerKeySelector, new CommonExpression.ExpTSC { _tables = _tables });
            this.InternalJoin(Expression.Lambda<Func<T1, TInner, bool>>(
                Expression.Equal(outerKeySelector.Body, innerKeySelector.Body),
                new[] { outerKeySelector.Parameters[0], innerKeySelector.Parameters[0] }
            ), SelectTableInfoType.InnerJoin);
            if (typeof(TResult) == typeof(T1)) return this as ISelect<TResult>;
            _selectExpression = resultSelector.Body;
            (_orm.CodeFirst as CodeFirstProvider)._dicSycedTryAdd(typeof(TResult)); //._dicSyced.TryAdd(typeof(TResult), true);
            var ret = _orm.Select<TResult>() as Select1Provider<TResult>;
            Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, null);
            return ret;
        }
        public ISelect<TResult> GroupJoin<TInner, TKey, TResult>(ISelect<TInner> inner, Expression<Func<T1, TKey>> outerKeySelector, Expression<Func<TInner, TKey>> innerKeySelector, Expression<Func<T1, ISelect<TInner>, TResult>> resultSelector) where TInner : class where TResult : class
        {
            _tables[0].Parameter = resultSelector.Parameters[0];
            _commonExpression.ExpressionLambdaToSql(outerKeySelector, new CommonExpression.ExpTSC { _tables = _tables });
            this.InternalJoin(Expression.Lambda<Func<T1, TInner, bool>>(
                Expression.Equal(outerKeySelector.Body, innerKeySelector.Body),
                new[] { outerKeySelector.Parameters[0], innerKeySelector.Parameters[0] }
            ), SelectTableInfoType.InnerJoin);
            if (typeof(TResult) == typeof(T1)) return this as ISelect<TResult>;
            _selectExpression = resultSelector.Body;
            (_orm.CodeFirst as CodeFirstProvider)._dicSycedTryAdd(typeof(TResult)); //._dicSyced.TryAdd(typeof(TResult), true);
            var ret = _orm.Select<TResult>() as Select1Provider<TResult>;
            Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, null);
            return ret;
        }
        public ISelect<TResult> SelectMany<TCollection, TResult>(Expression<Func<T1, ISelect<TCollection>>> collectionSelector, Expression<Func<T1, TCollection, TResult>> resultSelector) where TCollection : class where TResult : class
        {
            SelectTableInfo find = null;
            if (collectionSelector.Body.NodeType == ExpressionType.Call)
            {
                var callExp = collectionSelector.Body as MethodCallExpression;
                if (callExp.Method.Name == "DefaultIfEmpty" && callExp.Object.Type.GetGenericArguments().Any())
                {
                    find = _tables.Where((a, idx) => idx > 0 && a.Type == SelectTableInfoType.InnerJoin && a.Table.Type == callExp.Object.Type.GetGenericArguments()[0]).LastOrDefault();
                    if (find != null)
                    {
                        if (!string.IsNullOrEmpty(find.On)) find.On = Regex.Replace(find.On, $@"\b{find.Alias}\.", $"{resultSelector.Parameters[1].Name}.");
                        if (!string.IsNullOrEmpty(find.NavigateCondition)) find.NavigateCondition = Regex.Replace(find.NavigateCondition, $@"\b{find.Alias}\.", $"{resultSelector.Parameters[1].Name}.");
                        find.Type = SelectTableInfoType.LeftJoin;
                        find.Alias = resultSelector.Parameters[1].Name;
                        find.Parameter = resultSelector.Parameters[1];
                    }
                }
            }
            if (find == null)
            {
                var tb = _commonUtils.GetTableByEntity(typeof(TCollection));
                if (tb == null) throw new Exception($"SelectMany 错误的类型：{typeof(TCollection).FullName}");
                _tables.Add(new SelectTableInfo { Alias = resultSelector.Parameters[1].Name, AliasInit = resultSelector.Parameters[1].Name, Parameter = resultSelector.Parameters[1], Table = tb, Type = SelectTableInfoType.From });
            }
            if (typeof(TResult) == typeof(T1)) return this as ISelect<TResult>;
            _selectExpression = resultSelector.Body;
            (_orm.CodeFirst as CodeFirstProvider)._dicSycedTryAdd(typeof(TResult)); //._dicSyced.TryAdd(typeof(TResult), true);
            var ret = _orm.Select<TResult>() as Select1Provider<TResult>;
            Select0Provider<ISelect<T1>, T1>.CopyData(this, ret, null);
            return ret;
        }
        public ISelect<T1> DefaultIfEmpty()
        {
            return this;
        }
        #endregion

        public DataTable ToDataTable<TReturn>(Expression<Func<T1, TReturn>> select)
        {
            if (select == null) return this.InternalToDataTable(select?.Body);
            _tables[0].Parameter = select.Parameters[0];
            return this.InternalToDataTable(select?.Body);
        }

        public string ToSql<TReturn>(Expression<Func<T1, TReturn>> select, FieldAliasOptions fieldAlias = FieldAliasOptions.AsIndex)
        {
            if (select == null) return this.InternalToSql<TReturn>(select?.Body, fieldAlias);
            _tables[0].Parameter = select.Parameters[0];
            return this.InternalToSql<TReturn>(select?.Body, fieldAlias);
        }

        public TReturn ToAggregate<TReturn>(Expression<Func<ISelectGroupingAggregate<T1>, TReturn>> select)
        {
            if (select == null) return default(TReturn);
            _tables[0].Parameter = select.Parameters[0];
            return this.InternalToAggregate<TReturn>(select?.Body);
        }

        public ISelect<T1> Where(Expression<Func<T1, bool>> exp) => WhereIf(true, exp);
        public ISelect<T1> WhereIf(bool condition, Expression<Func<T1, bool>> exp)
        {
            if (condition == false || exp == null) return this;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalWhere(exp?.Body);
        }

        public ISelect<T1> Where<T2>(Expression<Func<T1, T2, bool>> exp) where T2 : class
        {
            if (exp == null) return this;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalWhere(exp?.Body);
        }

        public ISelect<T1> Where<T2>(Expression<Func<T2, bool>> exp) where T2 : class
        {
            if (exp == null) return this;
            //_tables[0].Parameter = exp.Parameters[0];
            return this.InternalWhere(exp?.Body);
        }

        public ISelect<T1> Where<T2, T3>(Expression<Func<T1, T2, T3, bool>> exp) where T2 : class where T3 : class
        {
            if (exp == null) return this;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalWhere(exp?.Body);
        }

        public ISelect<T1> Where<T2, T3, T4>(Expression<Func<T1, T2, T3, T4, bool>> exp) where T2 : class where T3 : class where T4 : class
        {
            if (exp == null) return this;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalWhere(exp?.Body);
        }

        public ISelect<T1> Where<T2, T3, T4, T5>(Expression<Func<T1, T2, T3, T4, T5, bool>> exp) where T2 : class where T3 : class where T4 : class where T5 : class
        {
            if (exp == null) return this;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalWhere(exp?.Body);
        }
        public ISelect<T1> WhereDynamic(object dywhere, bool not = false) => not == false ?
            this.Where(_commonUtils.WhereObject(_tables.First().Table, $"{_tables.First().Alias}.", dywhere)) :
            this.Where($"not({_commonUtils.WhereObject(_tables.First().Table, $"{_tables.First().Alias}.", dywhere)})");

        public ISelect<T1> WhereCascade(Expression<Func<T1, bool>> exp)
        {
            if (exp != null) _whereCascadeExpression.Add(exp);
            return this;
        }

        public ISelect<T1> WithSql(string sql)
        {
            this.AsTable((type, old) =>
            {
                if (type == _tables.First().Table?.Type) return $"( {sql} )";
                return old;
            });
            return this;
        }

        public bool Any(Expression<Func<T1, bool>> exp) => this.Where(exp).Any();

        public TReturn ToOne<TReturn>(Expression<Func<T1, TReturn>> select) => this.Limit(1).ToList(select).FirstOrDefault();
        public TDto ToOne<TDto>() => this.Limit(1).ToList<TDto>().FirstOrDefault();

        public TReturn First<TReturn>(Expression<Func<T1, TReturn>> select) => this.ToOne(select);
        public TDto First<TDto>() => this.ToOne<TDto>();

        public override List<T1> ToList(bool includeNestedMembers = false) => base.ToList(_isIncluded || includeNestedMembers);

        bool _isIncluded = false;
        public ISelect<T1> Include<TNavigate>(Expression<Func<T1, TNavigate>> navigateSelector) where TNavigate : class
        {
            var expBody = navigateSelector?.Body;
            if (expBody == null) return this;
            var tb = _commonUtils.GetTableByEntity(expBody.Type);
            if (tb == null) throw new Exception("Include 参数类型错误");

            _isIncluded = true;
            _commonExpression.ExpressionWhereLambda(_tables, Expression.MakeMemberAccess(expBody, tb.Properties[tb.ColumnsByCs.First().Value.CsName]), null, null, null);
            return this;
        }

        static (ParameterExpression param, List<MemberExpression> members) GetExpressionStack(Expression exp)
        {
            Expression tmpExp = exp;
            ParameterExpression param = null;
            var members = new List<MemberExpression>();
            var isbreak = false;
            while (isbreak == false)
            {
                switch (tmpExp.NodeType)
                {
                    case ExpressionType.MemberAccess:
                        var memExp = tmpExp as MemberExpression;
                        tmpExp = memExp.Expression;
                        members.Add(memExp);
                        continue;
                    case ExpressionType.Parameter:
                        param = tmpExp as ParameterExpression;
                        isbreak = true;
                        break;
                    default:
                        throw new Exception($"表达式错误，它不是连续的 MemberAccess 类型：{exp}");
                }
            }
            if (param == null) throw new Exception($"表达式错误，它的顶级对象不是 ParameterExpression：{exp}");
            return (param, members);
        }
        static MethodInfo GetEntityValueWithPropertyNameMethod = typeof(EntityUtilExtensions).GetMethod("GetEntityValueWithPropertyName");
        static ConcurrentDictionary<Type, ConcurrentDictionary<string, MethodInfo>> _dicTypeMethod = new ConcurrentDictionary<Type, ConcurrentDictionary<string, MethodInfo>>();
        public ISelect<T1> IncludeMany<TNavigate>(Expression<Func<T1, IEnumerable<TNavigate>>> navigateSelector, Action<ISelect<TNavigate>> then = null) where TNavigate : class
        {
            var throwNavigateSelector = new Exception("IncludeMany 参数1 类型错误，表达式类型应该为 MemberAccess");

            var expBody = navigateSelector?.Body;
            if (expBody == null) return this;
            if (expBody.NodeType == ExpressionType.Convert) expBody = (expBody as UnaryExpression)?.Operand; //- 兼容 Vb.Net 无法使用 IncludeMany 的问题；
            MethodCallExpression whereExp = null;
            int takeNumber = 0;
            Expression<Func<TNavigate, TNavigate>> selectExp = null;
            while (expBody.NodeType == ExpressionType.Call)
            {
                throwNavigateSelector = new Exception($"IncludeMany {nameof(navigateSelector)} 参数类型错误，正确格式： a.collections.Take(1).Where(c => c.aid == a.id).Select(a => new TNavigate {{ }})");
                var callExp = (expBody as MethodCallExpression);
                switch (callExp.Method.Name)
                {
                    case "Where":
                        whereExp = callExp;
                        break;
                    case "Take":
                        takeNumber = int.Parse(_commonExpression.ExpressionLambdaToSql(callExp.Arguments[1], new CommonExpression.ExpTSC { }));
                        break;
                    case "Select":
                        selectExp = (callExp.Arguments[1] as Expression<Func<TNavigate, TNavigate>>);
                        if (selectExp?.Parameters.Count != 1) throw new Exception($"IncludeMany {nameof(navigateSelector)} 参数错误，Select 只可以使用一个参数的方法，正确格式： .Select(t => new TNavigate {{ }})");
                        break;
                    default: throw throwNavigateSelector;
                }
                expBody = callExp.Object ?? callExp.Arguments.FirstOrDefault();
            }

            if (expBody.NodeType != ExpressionType.MemberAccess) throw throwNavigateSelector;
            var collMem = expBody as MemberExpression;
            var (membersParam, members) = GetExpressionStack(collMem.Expression);
            var tb = _commonUtils.GetTableByEntity(collMem.Expression.Type);
            if (tb == null) throw throwNavigateSelector;
            var collMemElementType = (collMem.Type.IsGenericType ? collMem.Type.GetGenericArguments().FirstOrDefault() : collMem.Type.GetElementType());
            if (typeof(TNavigate) != collMemElementType) 
                throw new Exception($"IncludeMany {nameof(navigateSelector)} 参数错误，Select lambda参数返回值必须和 {collMemElementType} 类型一致");
            var tbNav = _commonUtils.GetTableByEntity(typeof(TNavigate));
            if (tbNav == null) throw new Exception($"类型 {typeof(TNavigate).FullName} 错误，不能使用 IncludeMany");

            if (collMem.Expression.NodeType != ExpressionType.Parameter)
                _commonExpression.ExpressionWhereLambda(_tables, Expression.MakeMemberAccess(collMem.Expression, tb.Properties[tb.ColumnsByCs.First().Value.CsName]), null, null, null);

            TableRef tbref = null;
            var tbrefOneToManyColumns = new List<List<MemberExpression>>(); //临时 OneToMany 三个表关联，第三个表需要前两个表确定
            if (whereExp == null)
            {
                tbref = tb.GetTableRef(collMem.Member.Name, true);
            }
            else
            {
                //处理临时关系映射
                tbref = new TableRef
                {
                    RefType = TableRefType.OneToMany,
                    Property = tb.Properties[collMem.Member.Name],
                    RefEntityType = tbNav.Type
                };
                foreach (var whereExpArg in whereExp.Arguments)
                {
                    if (whereExpArg.NodeType != ExpressionType.Lambda) continue;
                    var whereExpArgLamb = whereExpArg as LambdaExpression;

                    Action<Expression> actWeiParse = null;
                    actWeiParse = expOrg =>
                    {
                        var binaryExp = expOrg as BinaryExpression;
                        if (binaryExp == null) throw throwNavigateSelector;

                        switch (binaryExp.NodeType)
                        {
                            case ExpressionType.AndAlso:
                                actWeiParse(binaryExp.Left);
                                actWeiParse(binaryExp.Right);
                                break;
                            case ExpressionType.Equal:
                                Expression leftExp = binaryExp.Left;
                                Expression rightExp = binaryExp.Right;
                                while (leftExp.NodeType == ExpressionType.Convert) leftExp = (leftExp as UnaryExpression)?.Operand;
                                while (rightExp.NodeType == ExpressionType.Convert) rightExp = (rightExp as UnaryExpression)?.Operand;
                                var leftP1MemberExp = leftExp as MemberExpression;
                                var rightP1MemberExp = rightExp as MemberExpression;
                                if (leftP1MemberExp == null || rightP1MemberExp == null) throw throwNavigateSelector;

                                if (leftP1MemberExp.Expression == whereExpArgLamb.Parameters[0])
                                {
                                    var (rightMembersParam, rightMembers) = GetExpressionStack(rightP1MemberExp.Expression);
                                    if (rightMembersParam != membersParam) throw throwNavigateSelector;
                                    var isCollMemEquals = rightMembers.Count == members.Count;
                                    if (isCollMemEquals)
                                    {
                                        for (var l = 0; l < members.Count; l++)
                                            if (members[l].Member != rightMembers[l].Member)
                                            {
                                                isCollMemEquals = false;
                                                break;
                                            }
                                    }
                                    if (isCollMemEquals)
                                    {
                                        tbref.Columns.Add(tb.ColumnsByCs[rightP1MemberExp.Member.Name]);
                                        tbrefOneToManyColumns.Add(null);
                                    }
                                    else
                                    {
                                        var tmpTb = _commonUtils.GetTableByEntity(rightP1MemberExp.Expression.Type);
                                        if (tmpTb == null) throw throwNavigateSelector;
                                        tbref.Columns.Add(tmpTb.ColumnsByCs[rightP1MemberExp.Member.Name]);
                                        tbrefOneToManyColumns.Add(rightMembers);
                                    }
                                    tbref.RefColumns.Add(tbNav.ColumnsByCs[leftP1MemberExp.Member.Name]);
                                    return;
                                }
                                if (rightP1MemberExp.Expression == whereExpArgLamb.Parameters[0])
                                {
                                    var (leftMembersParam, leftMembers) = GetExpressionStack(leftP1MemberExp.Expression);
                                    if (leftMembersParam != membersParam) throw throwNavigateSelector;
                                    var isCollMemEquals = leftMembers.Count == members.Count;
                                    if (isCollMemEquals)
                                    {
                                        for (var l = 0; l < members.Count; l++)
                                            if (members[l].Member != leftMembers[l].Member)
                                            {
                                                isCollMemEquals = false;
                                                break;
                                            }
                                    }
                                    if (isCollMemEquals)
                                    {
                                        tbref.Columns.Add(tb.ColumnsByCs[leftP1MemberExp.Member.Name]);
                                        tbrefOneToManyColumns.Add(null);
                                    }
                                    else
                                    {
                                        var tmpTb = _commonUtils.GetTableByEntity(leftP1MemberExp.Expression.Type);
                                        if (tmpTb == null) throw throwNavigateSelector;
                                        tbref.Columns.Add(tmpTb.ColumnsByCs[leftP1MemberExp.Member.Name]);
                                        tbrefOneToManyColumns.Add(leftMembers);
                                    }
                                    tbref.RefColumns.Add(tbNav.ColumnsByCs[rightP1MemberExp.Member.Name]);
                                    return;
                                }

                                throw throwNavigateSelector;
                            default: throw throwNavigateSelector;
                        }
                    };
                    actWeiParse(whereExpArgLamb.Body);
                    break;
                }
                if (tbref.Columns.Any() == false) throw throwNavigateSelector;
            }

#if net40
            Action<object, bool> includeToListSyncOrAsync = (listObj, isAsync) =>
            {
                isAsync = false;
#else
            Func<object, bool, Task> includeToListSyncOrAsync = async (listObj, isAsync) =>
            {
#endif

                var list = listObj as List<T1>;
                if (list == null) return;
                if (list.Any() == false) return;
                if (tbref.Columns.Any() == false) return;

                var t1parm = Expression.Parameter(typeof(T1));
                Expression membersExp = t1parm;
                Expression membersExpNotNull = null;
                foreach (var mem in members)
                {
                    membersExp = Expression.MakeMemberAccess(membersExp, mem.Member);
                    var expNotNull = Expression.NotEqual(membersExp, Expression.Constant(null));
                    if (membersExpNotNull == null) membersExpNotNull = expNotNull;
                    else membersExpNotNull = Expression.AndAlso(membersExpNotNull, expNotNull);
                }
                members.Clear();

                var listValueExp = Expression.Parameter(typeof(List<TNavigate>), "listValue");
                var setListValue = membersExpNotNull == null ?
                    Expression.Lambda<Action<T1, List<TNavigate>>>(
                        Expression.Assign(
                            Expression.MakeMemberAccess(membersExp, collMem.Member),
                            Expression.TypeAs(listValueExp, collMem.Type)
                        ), t1parm, listValueExp).Compile() :
                    Expression.Lambda<Action<T1, List<TNavigate>>>(
                        Expression.IfThen(
                            membersExpNotNull,
                            Expression.Assign(
                                Expression.MakeMemberAccess(membersExp, collMem.Member),
                                Expression.TypeAs(listValueExp, collMem.Type)
                            )
                        ), t1parm, listValueExp).Compile();

                var returnTarget = Expression.Label(typeof(object));
                var propertyNameExp = Expression.Parameter(typeof(string), "propertyName");
                var getListValue1 = membersExpNotNull == null ?
                    Expression.Lambda<Func<T1, string, object>>(
                        Expression.Block(
                            Expression.Return(returnTarget, Expression.Call(null, GetEntityValueWithPropertyNameMethod, Expression.Constant(_orm), Expression.Constant(membersExp.Type), membersExp, propertyNameExp)),
                            Expression.Label(returnTarget, Expression.Default(typeof(object)))
                        ), t1parm, propertyNameExp).Compile() :
                    Expression.Lambda<Func<T1, string, object>>(
                        Expression.Block(
                            Expression.IfThen(
                                membersExpNotNull,
                                Expression.Return(returnTarget, Expression.Call(null, GetEntityValueWithPropertyNameMethod, Expression.Constant(_orm), Expression.Constant(membersExp.Type), membersExp, propertyNameExp))
                            ),
                            Expression.Label(returnTarget, Expression.Default(typeof(object)))
                        ), t1parm, propertyNameExp).Compile();

                var getListValue2 = new List<Func<T1, string, object>>();
                for (var j = 0; j < tbrefOneToManyColumns.Count; j++)
                {
                    if (tbrefOneToManyColumns[j] == null)
                    {
                        getListValue2.Add(null);
                        continue;
                    }
                    Expression tbrefOneToManyColumnsMembers = t1parm;
                    Expression tbrefOneToManyColumnsMembersNotNull = null;
                    foreach (var mem in tbrefOneToManyColumns[j])
                    {
                        tbrefOneToManyColumnsMembers = Expression.MakeMemberAccess(tbrefOneToManyColumnsMembers, mem.Member);
                        var expNotNull = Expression.NotEqual(membersExp, Expression.Constant(null));
                        if (tbrefOneToManyColumnsMembersNotNull == null) tbrefOneToManyColumnsMembersNotNull = expNotNull;
                        else tbrefOneToManyColumnsMembersNotNull = Expression.AndAlso(tbrefOneToManyColumnsMembersNotNull, expNotNull);
                    }
                    tbrefOneToManyColumns[j].Clear();
                    getListValue2.Add(tbrefOneToManyColumnsMembersNotNull == null ?
                        Expression.Lambda<Func<T1, string, object>>(
                            Expression.Block(
                                Expression.Return(returnTarget, Expression.Call(null, GetEntityValueWithPropertyNameMethod, Expression.Constant(_orm), Expression.Constant(tbrefOneToManyColumnsMembers.Type), tbrefOneToManyColumnsMembers, propertyNameExp)),
                                Expression.Label(returnTarget, Expression.Default(typeof(object)))
                            ), t1parm, propertyNameExp).Compile() :
                        Expression.Lambda<Func<T1, string, object>>(
                            Expression.Block(
                                Expression.IfThen(
                                    tbrefOneToManyColumnsMembersNotNull,
                                    Expression.Return(returnTarget, Expression.Call(null, GetEntityValueWithPropertyNameMethod, Expression.Constant(_orm), Expression.Constant(tbrefOneToManyColumnsMembers.Type), tbrefOneToManyColumnsMembers, propertyNameExp))
                                ),
                                Expression.Label(returnTarget, Expression.Default(typeof(object)))
                            ), t1parm, propertyNameExp).Compile());
                }
                tbrefOneToManyColumns.Clear();
                Func<T1, string, int, object> getListValue = (item, propName, colIndex) =>
                {
                    if (getListValue2.Any() && getListValue2[colIndex] != null) return getListValue2[colIndex](item, propName);
                    return getListValue1(item, propName);
                };

                foreach (var item in list)
                    setListValue(item, null);

                var subSelect = _orm.Select<TNavigate>()
                    .DisableGlobalFilter()
                    .WithConnection(_connection)
                    .WithTransaction(_transaction)
                    .TrackToList(_trackToList) as Select1Provider<TNavigate>;
                if (_tableRules?.Any() == true)
                    foreach (var tr in _tableRules) subSelect.AsTable(tr);

                if (_whereCascadeExpression.Any())
                    subSelect._whereCascadeExpression.AddRange(_whereCascadeExpression.ToArray());

                //subSelect._aliasRule = _aliasRule; //把 SqlServer 查询锁传递下去
                then?.Invoke(subSelect);
                var subSelectT1Alias = subSelect._tables[0].Alias;
                var oldWhere = subSelect._where.ToString();
                if (oldWhere.StartsWith(" AND ")) oldWhere = oldWhere.Remove(0, 5);

                if (selectExp != null)
                {
                    var tmpinitExp = selectExp.Body as MemberInitExpression;
                    var newinitExpBindings = tmpinitExp.Bindings.ToList();
                    foreach (var tbrefCol in tbref.RefColumns)
                    {
                        if (newinitExpBindings.Any(a => a.Member.Name == tbrefCol.CsName)) continue;
                        var tmpMemberInfo = tbrefCol.Table.Properties[tbrefCol.CsName];
                        newinitExpBindings.Add(Expression.Bind(tmpMemberInfo, Expression.MakeMemberAccess(selectExp.Parameters[0], tmpMemberInfo)));
                    }
                    Expression newinitExp = Expression.MemberInit(tmpinitExp.NewExpression, newinitExpBindings.ToList());
                    var selectExpParam = subSelect._tables[0].Parameter ?? Expression.Parameter(typeof(TNavigate), subSelectT1Alias);
                    newinitExp = new NewExpressionVisitor(selectExpParam, selectExp.Parameters[0]).Replace(newinitExp);
                    selectExp = Expression.Lambda<Func<TNavigate, TNavigate>>(newinitExp, selectExpParam);
                }

                switch (tbref.RefType)
                {
                    case TableRefType.OneToMany:
                        if (true)
                        {
                            var subList = new List<TNavigate>();
                            var tbref2 = _commonUtils.GetTableByEntity(tbref.RefEntityType);
                            Func<Dictionary<string, bool>> getWhereDic = () =>
                            {
                                var sbDic = new Dictionary<string, bool>();
                                for (var y = 0; y < list.Count; y++)
                                {
                                    var sbWhereOne = new StringBuilder();
                                    sbWhereOne.Append("(");
                                    for (var z = 0; z < tbref.Columns.Count; z++)
                                    {
                                        if (z > 0) sbWhereOne.Append(" AND ");
                                        sbWhereOne.Append(_commonUtils.FormatSql($"{subSelectT1Alias}.{_commonUtils.QuoteSqlName(tbref.RefColumns[z].Attribute.Name)}={{0}}", Utils.GetDataReaderValue(tbref.RefColumns[z].Attribute.MapType, getListValue(list[y], tbref.Columns[z].CsName, z))));
                                    }
                                    sbWhereOne.Append(")");
                                    var whereOne = sbWhereOne.ToString();
                                    sbWhereOne.Clear();
                                    if (sbDic.ContainsKey(whereOne) == false) sbDic.Add(whereOne, true);
                                }
                                return sbDic;
                            };
                            if (takeNumber > 0)
                            {
                                Select0Provider<ISelect<TNavigate>, TNavigate>.GetAllFieldExpressionTreeInfo af = null;
                                (ReadAnonymousTypeInfo map, string field) mf = default;
                                if (selectExp == null) af = subSelect.GetAllFieldExpressionTreeLevelAll();
                                else mf = subSelect.GetExpressionField(selectExp);
                                var sbSql = new StringBuilder();
                                var sbDic = getWhereDic();
                                foreach (var sbd in sbDic)
                                {
                                    subSelect._where.Clear();
                                    subSelect.Where(sbd.Key).Where(oldWhere).Limit(takeNumber);
                                    sbSql.Append("\r\nUNION ALL\r\nselect * from (").Append(subSelect.ToSql(selectExp == null ? af.Field : mf.field)).Append(") ftb");
                                }
                                sbSql.Remove(0, 13);
                                if (sbDic.Count == 1) sbSql.Remove(0, 15).Remove(sbSql.Length - 5, 5);
                                sbDic.Clear();

                                if (isAsync)
                                {
#if net40
#else
                                    if (selectExp == null) subList = await subSelect.ToListAfPrivateAsync(sbSql.ToString(), af, null);
                                    else subList = await subSelect.ToListMrPrivateAsync<TNavigate>(sbSql.ToString(), mf, null);
#endif
                                }
                                else
                                {
                                    if (selectExp == null) subList = subSelect.ToListAfPrivate(sbSql.ToString(), af, null);
                                    else subList = subSelect.ToListMrPrivate<TNavigate>(sbSql.ToString(), mf, null);
                                }
                                sbSql.Clear();
                            }
                            else
                            {
                                subSelect._where.Clear();
                                if (tbref.Columns.Count == 1)
                                {
                                    var arrExp = Expression.NewArrayInit(tbref.RefColumns[0].CsType,
                                        list.Select(a => getListValue(a, tbref.Columns[0].CsName, 0)).Distinct()
                                            .Select(a => Expression.Constant(Utils.GetDataReaderValue(tbref.RefColumns[0].CsType, a), tbref.RefColumns[0].CsType)).ToArray());
                                    var otmExpParm1 = Expression.Parameter(typeof(TNavigate), "a");
                                    var containsMethod = _dicTypeMethod.GetOrAdd(tbref.RefColumns[0].CsType, et => new ConcurrentDictionary<string, MethodInfo>()).GetOrAdd("Contains", mn =>
                                        typeof(Enumerable).GetMethods().Where(a => a.Name == mn).First()).MakeGenericMethod(tbref.RefColumns[0].CsType);
                                    var refCol = Expression.MakeMemberAccess(otmExpParm1, tbref2.Properties[tbref.RefColumns[0].CsName]);
                                    //if (refCol.Type.IsNullableType()) refCol = Expression.Property(refCol, CommonExpression._dicNullableValueProperty.GetOrAdd(refCol.Type, ct1 => ct1.GetProperty("Value")));
                                    subSelect.Where(Expression.Lambda<Func<TNavigate, bool>>(
                                        Expression.Call(null, containsMethod, arrExp, refCol), otmExpParm1));
                                }
                                else
                                {
                                    var sbDic = getWhereDic();
                                    var sbWhere = new StringBuilder();
                                    foreach (var sbd in sbDic)
                                        sbWhere.Append(" OR ").Append(sbd.Key);
                                    subSelect.Where(sbWhere.Remove(0, 4).ToString());
                                    sbWhere.Clear();
                                    sbDic.Clear();
                                }
                                subSelect.Where(oldWhere);

                                if (isAsync)
                                {
#if net40
#else
                                    if (selectExp == null) subList = await subSelect.ToListAsync(true);
                                    else subList = await subSelect.ToListAsync<TNavigate>(selectExp);
#endif
                                }
                                else
                                {
                                    if (selectExp == null) subList = subSelect.ToList(true);
                                    else subList = subSelect.ToList<TNavigate>(selectExp);
                                }
                            }

                            if (subList.Any() == false)
                            {
                                foreach (var item in list)
                                    setListValue(item, new List<TNavigate>());
                                return;
                            }

                            Dictionary<string, List<Tuple<T1, List<TNavigate>>>> dicList = new Dictionary<string, List<Tuple<T1, List<TNavigate>>>>();
                            foreach (var item in list)
                            {
                                if (tbref.Columns.Count == 1)
                                {
                                    var dicListKey = getListValue(item, tbref.Columns[0].CsName, 0)?.ToString();
                                    if (dicListKey == null) continue;
                                    var dicListVal = Tuple.Create(item, new List<TNavigate>());
                                    if (dicList.TryGetValue(dicListKey, out var items) == false)
                                        dicList.Add(dicListKey, items = new List<Tuple<T1, List<TNavigate>>>());
                                    items.Add(dicListVal);
                                }
                                else
                                {
                                    var sb = new StringBuilder();
                                    for (var z = 0; z < tbref.Columns.Count; z++)
                                    {
                                        if (z > 0) sb.Append("*$*");
                                        sb.Append(getListValue(item, tbref.Columns[z].CsName, z));
                                    }
                                    var dicListKey = sb.ToString();
                                    var dicListVal = Tuple.Create(item, new List<TNavigate>());
                                    if (dicList.TryGetValue(dicListKey, out var items) == false)
                                        dicList.Add(dicListKey, items = new List<Tuple<T1, List<TNavigate>>>());
                                    items.Add(dicListVal);
                                    sb.Clear();
                                }
                            }
                            var parentNavs = new List<string>();
                            foreach (var navProp in tbref2.Properties)
                            {
                                if (tbref2.ColumnsByCs.ContainsKey(navProp.Key)) continue;
                                if (tbref2.ColumnsByCsIgnore.ContainsKey(navProp.Key)) continue;
                                var tr2ref = tbref2.GetTableRef(navProp.Key, false);
                                if (tr2ref == null) continue;
                                if (tr2ref.RefType != TableRefType.ManyToOne) continue;
                                if (tr2ref.RefEntityType != tb.Type) continue;
                                parentNavs.Add(navProp.Key);
                            }
                            foreach (var nav in subList)
                            {
                                string key = null;
                                if (tbref.RefColumns.Count == 1)
                                {
                                    key = _orm.GetEntityValueWithPropertyName(tbref.RefEntityType, nav, tbref.RefColumns[0].CsName).ToString();
                                }
                                else
                                {
                                    var sb = new StringBuilder();
                                    for (var z = 0; z < tbref.RefColumns.Count; z++)
                                    {
                                        if (z > 0) sb.Append("*$*");
                                        sb.Append(_orm.GetEntityValueWithPropertyName(tbref.RefEntityType, nav, tbref.RefColumns[z].CsName));
                                    }
                                    key = sb.ToString();
                                    sb.Clear();
                                }
                                if (dicList.TryGetValue(key, out var t1items) == false) return;
                                foreach (var t1item in t1items)
                                    t1item.Item2.Add(nav);

                                //将子集合的，多对一，对象设置为当前对象
                                foreach (var parentNav in parentNavs)
                                    foreach (var t1item in t1items)
                                        _orm.SetEntityValueWithPropertyName(tbref.RefMiddleEntityType, nav, parentNav, t1item.Item1);
                            }
                            foreach (var t1items in dicList.Values)
                                foreach (var t1item in t1items)
                                    setListValue(t1item.Item1, t1item.Item2);
                            dicList.Clear();
                        }
                        break;
                    case TableRefType.ManyToMany:
                        if (true)
                        {
                            List<TNavigate> subList = null;
                            List<object> midList = new List<object>();
                            var tbref2 = _commonUtils.GetTableByEntity(tbref.RefEntityType);
                            var tbrefMid = _commonUtils.GetTableByEntity(tbref.RefMiddleEntityType);
                            var sbJoin = new StringBuilder().Append($"{_commonUtils.QuoteSqlName(tbrefMid.DbName)} midtb ON ");
                            for (var z = 0; z < tbref.RefColumns.Count; z++)
                            {
                                if (z > 0) sbJoin.Append(" AND ");
                                sbJoin.Append($"midtb.{_commonUtils.QuoteSqlName(tbref.MiddleColumns[tbref.Columns.Count + z].Attribute.Name)} = a.{_commonUtils.QuoteSqlName(tbref.RefColumns[z].Attribute.Name)}");
                                if (_whereCascadeExpression.Any())
                                {
                                    var cascade = _commonExpression.GetWhereCascadeSql(new SelectTableInfo { Alias = "midtb", AliasInit = "midtb", Table = tbrefMid, Type = SelectTableInfoType.InnerJoin }, _whereCascadeExpression);
                                    if (string.IsNullOrEmpty(cascade) == false)
                                        sbJoin.Append(" AND (").Append(cascade).Append(")");
                                }
                            }
                            subSelect.InnerJoin(sbJoin.ToString());
                            sbJoin.Clear();

                            Select0Provider<ISelect<TNavigate>, TNavigate>.GetAllFieldExpressionTreeInfo af = null;
                            (ReadAnonymousTypeInfo map, string field) mf = default;
                            if (selectExp == null) af = subSelect.GetAllFieldExpressionTreeLevelAll();
                            else mf = subSelect.GetExpressionField(selectExp);
                            (string field, ReadAnonymousTypeInfo read)? otherData = null;
                            var sbSql = new StringBuilder();

                            if (_selectExpression == null)
                            {
                                var field = new StringBuilder();
                                var read = new ReadAnonymousTypeInfo();
                                read.CsType = (tbrefMid.TypeLazy ?? tbrefMid.Type);
                                read.Consturctor = read.CsType.InternalGetTypeConstructor0OrFirst();
                                read.IsEntity = true;
                                read.Table = tbrefMid;
                                foreach (var col in tbrefMid.Columns.Values)
                                {
                                    if (tbref.MiddleColumns.Where(a => a.CsName == col.CsName).Any() == false) continue;
                                    var child = new ReadAnonymousTypeInfo
                                    {
                                        CsName = col.CsName,
                                        CsType = col.CsType,
                                        DbField = $"midtb.{_commonUtils.QuoteSqlName(col.Attribute.Name)}",
                                        MapType = col.Attribute.MapType,
                                        Property = tbrefMid.Properties[col.CsName]
                                    };
                                    read.Childs.Add(child);
                                    field.Append(", ").Append(_commonUtils.QuoteReadColumn(child.CsType, child.MapType, child.DbField));
                                }
                                otherData = (field.ToString(), read);
                            }
                            Func<Dictionary<string, bool>> getWhereDic = () =>
                            {
                                var sbDic = new Dictionary<string, bool>();
                                for (var y = 0; y < list.Count; y++)
                                {
                                    var sbWhereOne = new StringBuilder();
                                    sbWhereOne.Append("(");
                                    for (var z = 0; z < tbref.Columns.Count; z++)
                                    {
                                        if (z > 0) sbWhereOne.Append(" AND ");
                                        sbWhereOne.Append(_commonUtils.FormatSql($" midtb.{_commonUtils.QuoteSqlName(tbref.MiddleColumns[z].Attribute.Name)}={{0}}", Utils.GetDataReaderValue(tbref.MiddleColumns[z].Attribute.MapType, getListValue1(list[y], tbref.Columns[z].CsName))));
                                    }
                                    sbWhereOne.Append(")");
                                    var whereOne = sbWhereOne.ToString();
                                    sbWhereOne.Clear();
                                    if (sbDic.ContainsKey(whereOne) == false) sbDic.Add(whereOne, true);
                                }
                                return sbDic;
                            };

                            if (takeNumber > 0)
                            {
                                var sbDic = getWhereDic();
                                foreach (var sbd in sbDic)
                                {
                                    subSelect._where.Clear();
                                    subSelect.Where(sbd.Key).Where(oldWhere).Limit(takeNumber);
                                    sbSql.Append("\r\nUNION ALL\r\nselect * from (").Append(subSelect.ToSql($"{(selectExp == null ? af.Field : mf.field)}{otherData?.field}")).Append(") ftb");
                                }
                                sbSql.Remove(0, 13);
                                if (sbDic.Count == 1) sbSql.Remove(0, 15).Remove(sbSql.Length - 5, 5);
                                sbDic.Clear();
                            }
                            else
                            {
                                subSelect._where.Clear();
                                if (tbref.Columns.Count == 1)
                                {
                                    subSelect.Where(_commonUtils.FormatSql($"midtb.{_commonUtils.QuoteSqlName(tbref.MiddleColumns[0].Attribute.Name)} in {{0}}", list.Select(a => Utils.GetDataReaderValue(tbref.MiddleColumns[0].Attribute.MapType, getListValue1(a, tbref.Columns[0].CsName))).Distinct()));
                                }
                                else
                                {
                                    var sbDic = getWhereDic();
                                    var sbWhere = new StringBuilder();
                                    foreach (var sbd in sbDic)
                                        sbWhere.Append(" OR ").Append(sbd.Key);
                                    subSelect.Where(sbWhere.Remove(0, 4).ToString());
                                    sbWhere.Clear();
                                    sbDic.Clear();
                                }
                                subSelect.Where(oldWhere);
                                sbSql.Append(subSelect.ToSql($"{(selectExp == null ? af.Field : mf.field)}{otherData?.field}"));
                            }

                            if (isAsync)
                            {
#if net40
#else
                                if (selectExp == null) subList = await subSelect.ToListAfPrivateAsync(sbSql.ToString(), af, otherData == null ? null : new[] { (otherData.Value.field, otherData.Value.read, midList) });
                                else subList = await subSelect.ToListMrPrivateAsync<TNavigate>(sbSql.ToString(), mf, otherData == null ? null : new[] { (otherData.Value.field, otherData.Value.read, midList) });
#endif
                            }
                            else
                            {
                                if (selectExp == null) subList = subSelect.ToListAfPrivate(sbSql.ToString(), af, otherData == null ? null : new[] { (otherData.Value.field, otherData.Value.read, midList) });
                                else subList = subSelect.ToListMrPrivate<TNavigate>(sbSql.ToString(), mf, otherData == null ? null : new[] { (otherData.Value.field, otherData.Value.read, midList) });
                            }
                            if (subList.Any() == false)
                            {
                                foreach (var item in list)
                                    setListValue(item, new List<TNavigate>());
                                return;
                            }

                            Dictionary<string, List<Tuple<T1, List<TNavigate>>>> dicList = new Dictionary<string, List<Tuple<T1, List<TNavigate>>>>();
                            foreach (var item in list)
                            {
                                if (tbref.Columns.Count == 1)
                                {
                                    var dicListKey = getListValue1(item, tbref.Columns[0].CsName)?.ToString();
                                    if (dicListKey == null) continue;
                                    var dicListVal = Tuple.Create(item, new List<TNavigate>());
                                    if (dicList.TryGetValue(dicListKey, out var items) == false)
                                        dicList.Add(dicListKey, items = new List<Tuple<T1, List<TNavigate>>>());
                                    items.Add(dicListVal);
                                }
                                else
                                {
                                    var sb = new StringBuilder();
                                    for (var z = 0; z < tbref.Columns.Count; z++)
                                    {
                                        if (z > 0) sb.Append("*$*");
                                        sb.Append(getListValue1(item, tbref.Columns[z].CsName));
                                    }
                                    var dicListKey = sb.ToString();
                                    var dicListVal = Tuple.Create(item, new List<TNavigate>());
                                    if (dicList.TryGetValue(dicListKey, out var items) == false)
                                        dicList.Add(dicListKey, items = new List<Tuple<T1, List<TNavigate>>>());
                                    items.Add(dicListVal);
                                    sb.Clear();
                                }
                            }
                            for (var a = 0; a < subList.Count; a++)
                            {
                                string key = null;
                                if (tbref.Columns.Count == 1)
                                    key = _orm.GetEntityValueWithPropertyName(tbref.RefMiddleEntityType, midList[a], tbref.MiddleColumns[0].CsName).ToString();
                                else
                                {
                                    var sb = new StringBuilder();
                                    for (var z = 0; z < tbref.Columns.Count; z++)
                                    {
                                        if (z > 0) sb.Append("*$*");
                                        sb.Append(_orm.GetEntityValueWithPropertyName(tbref.RefMiddleEntityType, midList[a], tbref.MiddleColumns[z].CsName));
                                    }
                                    key = sb.ToString();
                                    sb.Clear();
                                }
                                if (dicList.TryGetValue(key, out var t1items) == false) return;
                                foreach (var t1item in t1items)
                                    t1item.Item2.Add(subList[a]);
                            }
                            foreach (var t1items in dicList.Values)
                                foreach (var t1item in t1items)
                                    setListValue(t1item.Item1, t1item.Item2);
                            dicList.Clear();
                        }
                        break;
                }
            };

            _includeToList.Add(listObj => includeToListSyncOrAsync(listObj, false));
#if net40
#else
            _includeToListAsync.Add(listObj => includeToListSyncOrAsync(listObj, true));
#endif
            return this;
        }

        internal void SetList(IEnumerable<T1> list)
        {
            foreach (var include in _includeToList) include?.Invoke(list);
            _trackToList?.Invoke(list);
        }

#if net40
#else
        async internal Task SetListAsync(IEnumerable<T1> list)
        {
            foreach (var include in _includeToListAsync) await include?.Invoke(list);
            _trackToList?.Invoke(list);
        }

        public Task<double> AvgAsync<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return Task.FromResult(default(double));
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalAvgAsync(column?.Body);
        }
        public Task<TMember> MaxAsync<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return Task.FromResult(default(TMember));
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalMaxAsync<TMember>(column?.Body);
        }
        public Task<TMember> MinAsync<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return Task.FromResult(default(TMember));
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalMinAsync<TMember>(column?.Body);
        }
        public Task<decimal> SumAsync<TMember>(Expression<Func<T1, TMember>> column)
        {
            if (column == null) return Task.FromResult(default(decimal));
            _tables[0].Parameter = column.Parameters[0];
            return this.InternalSumAsync(column?.Body);
        }
        public Task<List<TReturn>> ToListAsync<TReturn>(Expression<Func<T1, TReturn>> select)
        {
            if (select == null) return this.InternalToListAsync<TReturn>(select?.Body);
            _tables[0].Parameter = select.Parameters[0];
            return this.InternalToListAsync<TReturn>(select?.Body);
        }
        public Task<List<TDto>> ToListAsync<TDto>() => ToListAsync(GetToListDtoSelector<TDto>());

        public Task<DataTable> ToDataTableAsync<TReturn>(Expression<Func<T1, TReturn>> select)
        {
            if (select == null) return this.InternalToDataTableAsync(select?.Body);
            _tables[0].Parameter = select.Parameters[0];
            return this.InternalToDataTableAsync(select?.Body);
        }
        public Task<TReturn> ToAggregateAsync<TReturn>(Expression<Func<ISelectGroupingAggregate<T1>, TReturn>> select)
        {
            if (select == null) return Task.FromResult(default(TReturn));
            _tables[0].Parameter = select.Parameters[0];
            return this.InternalToAggregateAsync<TReturn>(select?.Body);
        }

        public Task<bool> AnyAsync(Expression<Func<T1, bool>> exp) => this.Where(exp).AnyAsync();
        async public Task<TReturn> ToOneAsync<TReturn>(Expression<Func<T1, TReturn>> select) => (await this.Limit(1).ToListAsync(select)).FirstOrDefault();
        async public Task<TDto> ToOneAsync<TDto>() => (await this.Limit(1).ToListAsync<TDto>()).FirstOrDefault();
        public Task<TReturn> FirstAsync<TReturn>(Expression<Func<T1, TReturn>> select) => this.ToOneAsync(select);
        public Task<TDto> FirstAsync<TDto>() => this.ToOneAsync<TDto>();
        public override Task<List<T1>> ToListAsync(bool includeNestedMembers = false) => base.ToListAsync(_isIncluded || includeNestedMembers);
#endif
    }
}