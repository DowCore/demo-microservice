using System.Collections.Generic;
using System.Linq;
using Meta.Dow.SaaS.Orchestration;
using Volo.Abp;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// 表单拍平/克隆/校验（F1/F2/F5）。核心回归点：Layout 是唯一事实源、明细结构与联动配置不丢失。
/// </summary>
public class FormSchemaFlatteningTests
{
    [Fact]
    public void Flatten_overwrites_existing_field_properties()
    {
        var schema = new FormDef
        {
            Fields = [new FormFieldDef { Field = "amount", Title = "旧标题", RequiredOnCreate = false }],
            Layout =
            [
                new FormWidgetDef { Kind = "field", Field = "amount", Title = "金额", RequiredOnCreate = true },
            ],
        };

        FormDefinitionAppService.EnsureFlattenedFields(schema);

        var amount = Assert.Single(schema.Fields);
        Assert.Equal("金额", amount.Title);
        Assert.True(amount.RequiredOnCreate);
    }

    [Fact]
    public void Flatten_removes_fields_no_longer_in_layout()
    {
        var schema = new FormDef
        {
            Fields =
            [
                new FormFieldDef { Field = "amount", Title = "金额" },
                new FormFieldDef { Field = "ghost", Title = "幽灵字段" },
            ],
            Layout = [new FormWidgetDef { Kind = "field", Field = "amount", Title = "金额" }],
        };

        FormDefinitionAppService.EnsureFlattenedFields(schema);

        Assert.Single(schema.Fields);
        Assert.Equal("amount", schema.Fields[0].Field);
    }

    [Fact]
    public void Flatten_keeps_detail_container_children()
    {
        var schema = new FormDef
        {
            Layout =
            [
                new FormWidgetDef
                {
                    Kind = "container",
                    Container = "data-table",
                    Field = "items",
                    Title = "明细",
                    MinItems = 1,
                    MaxItems = 5,
                    Children =
                    [
                        new FormWidgetDef { Kind = "field", Field = "name", Title = "名称" },
                        new FormWidgetDef { Kind = "field", Field = "qty", Title = "数量" },
                    ],
                },
            ],
        };

        FormDefinitionAppService.EnsureFlattenedFields(schema);

        var detail = Assert.Single(schema.Fields);
        Assert.Equal("items", detail.Field);
        Assert.Equal(1, detail.MinItems);
        Assert.Equal(5, detail.MaxItems);
        Assert.Equal(2, detail.Children.Count);
        Assert.Equal(["name", "qty"], detail.Children.Select(x => x.Field).ToArray());
        // 明细子列不再扁平到顶层，避免与容器字段重复
        Assert.DoesNotContain(schema.Fields, f => f.Field == "name");
    }

    [Fact]
    public void Flatten_keeps_rules_and_dependencies()
    {
        var schema = new FormDef
        {
            Layout =
            [
                new FormWidgetDef
                {
                    Kind = "field",
                    Field = "email",
                    Title = "邮箱",
                    Rules = new FormFieldRuleDef { Pattern = "^\\S+@\\S+$", Message = "邮箱格式不正确" },
                    Dependencies = [new FormFieldDependencyDef { SourceField = "type", Op = "eq", Value = "A", Action = "show" }],
                    ControlProps = new Dictionary<string, object?> { ["placeholder"] = "请输入邮箱" },
                },
            ],
        };

        FormDefinitionAppService.EnsureFlattenedFields(schema);

        var email = Assert.Single(schema.Fields);
        Assert.NotNull(email.Rules);
        Assert.Equal("邮箱格式不正确", email.Rules!.Message);
        Assert.Single(email.Dependencies);
        Assert.Equal("请输入邮箱", email.ControlProps["placeholder"]);
    }

    [Fact]
    public void Clone_preserves_dependencies_and_control_props()
    {
        var widget = new FormWidgetDef
        {
            Kind = "field",
            Field = "level",
            Title = "等级",
            ColumnName = "grade",
            Dependencies = [new FormFieldDependencyDef { SourceField = "score", Op = "gte", Value = "90", Action = "required" }],
            ControlProps = new Dictionary<string, object?> { ["max"] = 100 },
        };

        var clone = FormDefinitionAppService.CloneWidget(widget);

        Assert.Equal("grade", clone.ColumnName);
        Assert.Single(clone.Dependencies);
        Assert.Equal(100, clone.ControlProps["max"]);
        // 深拷贝：改克隆不影响原对象
        clone.Dependencies.Clear();
        Assert.Single(widget.Dependencies);
    }

    [Fact]
    public void Validate_field_names_rejects_duplicates()
    {
        var schema = new FormDef
        {
            Fields =
            [
                new FormFieldDef { Field = "amount", Title = "金额" },
                new FormFieldDef { Field = "Amount", Title = "金额2" },
            ],
        };

        Assert.Throws<UserFriendlyException>(() => FormDefinitionAppService.ValidateFieldNames(schema));
    }

    [Fact]
    public void Validate_field_names_accepts_unique_names()
    {
        var schema = new FormDef
        {
            Fields =
            [
                new FormFieldDef { Field = "amount", Title = "金额" },
                new FormFieldDef { Field = "reason", Title = "事由" },
            ],
        };

        var exception = Record.Exception(() => FormDefinitionAppService.ValidateFieldNames(schema));
        Assert.Null(exception);
    }
}
