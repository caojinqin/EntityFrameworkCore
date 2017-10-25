﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;
using Remotion.Linq.Clauses;
using Remotion.Linq;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class CorrelatedCollectionMarkingExpressionVisitor : RelinqExpressionVisitor
    {
        private EntityQueryModelVisitor _queryModelVisitor;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public CorrelatedCollectionMarkingExpressionVisitor(EntityQueryModelVisitor queryModelVisitor)
        {
            _queryModelVisitor = queryModelVisitor;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public void CloneParentQueryModelForCorrelatedSubqueries(QueryModel parentQueryModel)
        {
            var correlatedSubqueryMetadataWithoutParentQueryModel = _queryModelVisitor.QueryCompilationContext.CorrelatedSubqueryMetadataMap.Where(m => m.Value.ClonedParentQueryModel == null);
            if (correlatedSubqueryMetadataWithoutParentQueryModel.Any())
            {
                var querySourceMapping = new QuerySourceMapping();
                var clonedParentQueryModel = parentQueryModel.Clone(querySourceMapping);
                clonedParentQueryModel.SelectClause = new SelectClause(Expression.Default(typeof(AnonymousObject2)));
                clonedParentQueryModel.ResultTypeOverride = typeof(IQueryable<>).MakeGenericType(clonedParentQueryModel.SelectClause.Selector.Type);

                // TODO: update annotations here also?

                foreach (var correlatedSubqueryMetadata in correlatedSubqueryMetadataWithoutParentQueryModel)
                {
                    correlatedSubqueryMetadata.Value.QuerySourceMapping = querySourceMapping;
                    correlatedSubqueryMetadata.Value.ClonedParentQueryModel = clonedParentQueryModel;
                }
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name.StartsWith("IncludeCollection"))
            {
                return node;
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitSubQuery(SubQueryExpression expression)
        {
            var subQueryModel = expression.QueryModel;

            if (subQueryModel.ResultOperators.Count == 0)
            {
                var querySourceReferenceFindingExpressionTreeVisitor
                    = new QuerySourceReferenceFindingExpressionTreeVisitor();

                querySourceReferenceFindingExpressionTreeVisitor.Visit(subQueryModel.SelectClause.Selector);
                if (querySourceReferenceFindingExpressionTreeVisitor.QuerySourceReferenceExpression?.ReferencedQuerySource == subQueryModel.MainFromClause)
                {
                    var newExpression = _queryModelVisitor.BindNavigationPathPropertyExpression(
                        subQueryModel.MainFromClause.FromExpression,
                        (properties, querySource) =>
                        {
                            var collectionNavigation = properties.OfType<INavigation>().SingleOrDefault(n => n.IsCollection());

                            if (collectionNavigation != null)
                            {
                                _queryModelVisitor.QueryCompilationContext.CorrelatedSubqueryMetadataMap[subQueryModel] = new QueryCompilationContext.CorrelatedSubqueryMetadata
                                {
                                    FirstNavigation = properties.OfType<INavigation>().First(),
                                    CollectionNavigation = collectionNavigation,
                                    ParentQuerySource = querySource
                                };

                                return expression;
                            }

                            return default;
                        });

                    if (newExpression != null)
                    {
                        return newExpression;
                    }
                }
            }

            return base.VisitSubQuery(expression);
        }
    }
}