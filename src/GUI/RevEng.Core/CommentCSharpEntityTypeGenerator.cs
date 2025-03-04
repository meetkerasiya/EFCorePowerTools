﻿using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Microsoft.EntityFrameworkCore.Scaffolding.Internal
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1202:Elements should be ordered by access", Justification = "Reviewed.")]
    public class CommentCSharpEntityTypeGenerator : ICSharpEntityTypeGenerator
    {
        private readonly ICSharpHelper code;
        private readonly bool nullableReferences;

        private IndentedStringBuilder sb;
        private bool useDataAnnotations;

        public CommentCSharpEntityTypeGenerator(
            [NotNull] ICSharpHelper cSharpHelper,
            bool nullableReferences)
        {
            code = cSharpHelper;
            this.nullableReferences = nullableReferences;
        }

        public string WriteCode(IEntityType entityType, string @namespace, bool useDataAnnotations)
        {
            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            sb = new IndentedStringBuilder();
            this.useDataAnnotations = useDataAnnotations;

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");

            if (this.useDataAnnotations)
            {
                sb.AppendLine("using System.ComponentModel.DataAnnotations;");
                sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
            }

            foreach (var ns in entityType.GetProperties()
                .SelectMany(p => p.ClrType.GetNamespaces())
                .Where(ns => ns != "System" && ns != "System.Collections.Generic")
                .Distinct()
                .OrderBy(x => x, new NamespaceComparer()))
            {
                sb.AppendLine($"using {ns};");
            }

            if (nullableReferences)
            {
                sb.AppendLine();
                sb.AppendLine("#nullable enable");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {@namespace}");
            sb.AppendLine("{");

            using (sb.Indent())
            {
                GenerateClass(entityType);
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        protected void GenerateClass(
            [NotNull] IEntityType entityType)
        {
            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            WriteComment(entityType.GetComment());

            if (useDataAnnotations)
            {
                GenerateEntityTypeDataAnnotations(entityType);
            }

            sb.AppendLine($"public partial class {entityType.Name}");

            sb.AppendLine("{");

            using (sb.Indent())
            {
                GenerateConstructor(entityType);
                GenerateProperties(entityType);
                GenerateNavigationProperties(entityType);
            }

            sb.AppendLine("}");
        }

        protected void GenerateEntityTypeDataAnnotations(
            [NotNull] IEntityType entityType)
        {
            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            GenerateTableAttribute(entityType);
        }

        private void GenerateTableAttribute(IEntityType entityType)
        {
            var tableName = entityType.GetTableName();
            var schema = entityType.GetSchema();
            var defaultSchema = entityType.Model.GetDefaultSchema();

            var schemaParameterNeeded = schema != null && schema != defaultSchema;
            var isView = entityType.FindAnnotation(RelationalAnnotationNames.ViewDefinition) != null;
            var tableAttributeNeeded = !isView && (schemaParameterNeeded || (tableName != null && tableName != entityType.GetDbSetName()));

            if (tableAttributeNeeded)
            {
                var tableAttribute = new AttributeWriter(nameof(TableAttribute));

                tableAttribute.AddParameter(code.Literal(tableName));

                if (schemaParameterNeeded)
                {
                    tableAttribute.AddParameter($"{nameof(TableAttribute.Schema)} = {code.Literal(schema)}");
                }

                sb.AppendLine(tableAttribute.ToString());
            }
        }

        protected void GenerateConstructor(
            [NotNull] IEntityType entityType)
        {
            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var collectionNavigations = entityType.GetNavigations().Where(n => n.IsCollection()).ToList();

            if (collectionNavigations.Count > 0)
            {
                sb.AppendLine($"public {entityType.Name}()");
                sb.AppendLine("{");

                using (sb.Indent())
                {
                    foreach (var navigation in collectionNavigations)
                    {
                        sb.AppendLine($"{navigation.Name} = new HashSet<{navigation.GetTargetType().Name}>();");
                    }
                }

                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        protected void GenerateProperties(
            [NotNull] IEntityType entityType)
        {
            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            foreach (var property in entityType.GetProperties().OrderBy(p => p.GetColumnOrdinal()))
            {
                WriteComment(property.GetComment());

                if (useDataAnnotations)
                {
                    GeneratePropertyDataAnnotations(property);
                }

                string nullableAnnotation = string.Empty;
                string defaultAnnotation = string.Empty;

                if (nullableReferences && !property.ClrType.IsValueType)
                {
                    if (property.IsColumnNullable())
                    {
                        nullableAnnotation = "?";
                    }
                    else
                    {
                        defaultAnnotation = $" = default!;";
                    }
                }

                sb.AppendLine($"public {code.Reference(property.ClrType)}{nullableAnnotation} {property.Name} {{ get; set; }}{defaultAnnotation}");
            }
        }

        protected void GeneratePropertyDataAnnotations(
            [NotNull] IProperty property)
        {
            if (property is null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            GenerateKeyAttribute(property);
            GenerateRequiredAttribute(property);
            GenerateColumnAttribute(property);
            GenerateMaxLengthAttribute(property);
        }

        private void WriteComment(string comment)
        {
            if (!string.IsNullOrWhiteSpace(comment))
            {
                sb.AppendLine("/// <summary>");

                foreach (var line in comment.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
                {
                    sb.AppendLine($"/// {System.Security.SecurityElement.Escape(line)}");
                }

                sb.AppendLine("/// </summary>");
            }
        }

        private void GenerateKeyAttribute(IProperty property)
        {
            var key = property.FindContainingPrimaryKey();
            if (key != null)
            {
                sb.AppendLine(new AttributeWriter(nameof(KeyAttribute)));
            }
        }

        private void GenerateColumnAttribute(IProperty property)
        {
            var columnName = property.GetColumnName();
            var columnType = property.GetConfiguredColumnType();

            var delimitedColumnName = columnName != null && columnName != property.Name ? code.Literal(columnName) : null;
            var delimitedColumnType = columnType != null ? code.Literal(columnType) : null;

            if ((delimitedColumnName ?? delimitedColumnType) != null)
            {
                var columnAttribute = new AttributeWriter(nameof(ColumnAttribute));

                if (delimitedColumnName != null)
                {
                    columnAttribute.AddParameter(delimitedColumnName);
                }

#pragma warning disable CA1508 // Avoid dead conditional code
                if (delimitedColumnType != null)
                {
                    columnAttribute.AddParameter($"{nameof(ColumnAttribute.TypeName)} = {delimitedColumnType}");
                }
#pragma warning restore CA1508 // Avoid dead conditional code
                sb.AppendLine(columnAttribute);
            }
        }

        private void GenerateMaxLengthAttribute(IProperty property)
        {
            var maxLength = property.GetMaxLength();

            if (maxLength.HasValue)
            {
                var lengthAttribute = new AttributeWriter(
                    property.ClrType == typeof(string)
                        ? nameof(StringLengthAttribute)
                        : nameof(MaxLengthAttribute));

                lengthAttribute.AddParameter(code.Literal(maxLength.Value));

                sb.AppendLine(lengthAttribute.ToString());
            }
        }

        private void GenerateRequiredAttribute(IProperty property)
        {
            if (!property.IsNullable
                && property.ClrType.IsNullableType()
                && !property.IsPrimaryKey())
            {
                sb.AppendLine(new AttributeWriter(nameof(RequiredAttribute)).ToString());
            }
        }

        protected void GenerateNavigationProperties(
            [NotNull] IEntityType entityType)
        {
            var sortedNavigations = entityType.GetNavigations()
                .OrderBy(n => n.IsDependentToPrincipal() ? 0 : 1)
                .ThenBy(n => n.IsCollection() ? 1 : 0)
                .ToList();

            if (sortedNavigations.Any())
            {
                sb.AppendLine();

                foreach (var navigation in sortedNavigations)
                {
                    if (useDataAnnotations)
                    {
                        GenerateNavigationDataAnnotations(navigation);
                    }

                    var referencedTypeName = navigation.GetTargetType().Name;
                    var navigationType = navigation.IsCollection() ? $"ICollection<{referencedTypeName}>" : referencedTypeName;

                    string nullableAnnotation = string.Empty;
                    string defaultAnnotation = string.Empty;

                    if (nullableReferences && !navigation.IsCollection())
                    {
                        if (navigation.ForeignKey?.IsRequired == true)
                        {
                            defaultAnnotation = $" = default!;";
                        }
                        else
                        {
                            nullableAnnotation = "?";
                        }
                    }

                    sb.AppendLine($"public virtual {navigationType}{nullableAnnotation} {navigation.Name} {{ get; set; }}{defaultAnnotation}");
                }
            }
        }

        private void GenerateNavigationDataAnnotations(INavigation navigation)
        {
            GenerateForeignKeyAttribute(navigation);
            GenerateInversePropertyAttribute(navigation);
        }

        private void GenerateForeignKeyAttribute(INavigation navigation)
        {
            if (navigation.IsDependentToPrincipal() && navigation.ForeignKey.PrincipalKey.IsPrimaryKey())
            {
                var foreignKeyAttribute = new AttributeWriter(nameof(ForeignKeyAttribute));

                if (navigation.ForeignKey.Properties.Count > 1)
                {
                    foreignKeyAttribute.AddParameter(
                        code.Literal(
                            string.Join(",", navigation.ForeignKey.Properties.Select(p => p.Name))));
                }
                else
                {
#pragma warning disable CA1826 // Do not use Enumerable methods on indexable collections
                    foreignKeyAttribute.AddParameter($"nameof({navigation.ForeignKey.Properties.First().Name})");
#pragma warning restore CA1826 // Do not use Enumerable methods on indexable collections
                }

                sb.AppendLine(foreignKeyAttribute.ToString());
            }
        }

        private void GenerateInversePropertyAttribute(INavigation navigation)
        {
            if (navigation.ForeignKey.PrincipalKey.IsPrimaryKey())
            {
                var inverseNavigation = navigation.FindInverse();

                if (inverseNavigation != null)
                {
                    var inversePropertyAttribute = new AttributeWriter(nameof(InversePropertyAttribute));

                    inversePropertyAttribute.AddParameter(
                        !navigation.DeclaringEntityType.GetPropertiesAndNavigations().Any(
                                m => m.Name == inverseNavigation.DeclaringEntityType.Name)
                            ? $"nameof({inverseNavigation.DeclaringEntityType.Name}.{inverseNavigation.Name})"
                            : code.Literal(inverseNavigation.Name));

                    sb.AppendLine(inversePropertyAttribute.ToString());
                }
            }
        }

        private sealed class AttributeWriter
        {
            private readonly string attributeName;
            private readonly List<string> parameters = new List<string>();

            public AttributeWriter([NotNull] string attributeName)
            {
                this.attributeName = attributeName;
            }

            public void AddParameter([NotNull] string parameter)
            {
                parameters.Add(parameter);
            }

            public override string ToString()
                => "[" + (parameters.Count == 0
                       ? StripAttribute(attributeName)
                       : StripAttribute(attributeName) + "(" + string.Join(", ", parameters) + ")") + "]";

            private static string StripAttribute([NotNull] string attributeName)
                => attributeName.EndsWith("Attribute", StringComparison.Ordinal)
                    ? attributeName.Substring(0, attributeName.Length - 9)
                    : attributeName;
        }
    }
}
