using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowDslDocument
{
    public string? Version { get; set; }

    public string? Key { get; set; }

    public List<FlowInputParameterDsl> Inputs { get; set; } = [];

    public List<FlowOutputParameterDsl> Outputs { get; set; } = [];

    public List<FlowDslNode> Nodes { get; set; } = [];

    public List<FlowDslEdge> Edges { get; set; } = [];
}

public class FlowInputParameterDsl
{
    public string Name { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string Type { get; set; } = "string";

    public bool Required { get; set; }

    public JsonNode? Default { get; set; }

    /// <summary>input | system | expr</summary>
    public string Source { get; set; } = "input";

    public string? SystemKey { get; set; }

    public string? SystemExpr { get; set; }

    public string? Description { get; set; }

    /// <summary>声明式校验规则（非 JS）</summary>
    public List<FlowInputRuleDsl>? Rules { get; set; }

    /// <summary>object 下行字段</summary>
    public List<FlowInputParameterDsl>? Properties { get; set; }

    /// <summary>array 元素 schema</summary>
    public FlowInputParameterDsl? Items { get; set; }
}

/// <summary>
/// 声明式入参规则。
/// type: required | min | max | minLength | maxLength | minItems | maxItems | pattern | enum | email | guid | url
///       | expr（表达式为真则失败）| assert（表达式为假则失败）
/// </summary>
public class FlowInputRuleDsl
{
    public string Type { get; set; } = null!;

    /// <summary>规则参数：数值、正则字符串、枚举数组、或布尔表达式字符串</summary>
    public JsonNode? Value { get; set; }

    public string? Message { get; set; }
}

public class FlowOutputParameterDsl
{
    public string Name { get; set; } = null!;

    public string Type { get; set; } = "string";

    public string? From { get; set; }

    public FlowVisibleToDsl? VisibleTo { get; set; }

    public bool Sensitive { get; set; }

    public string? Description { get; set; }

    /// <summary>array 元素字段映射；省略则 list 原样透传</summary>
    public FlowOutputMapDsl? Map { get; set; }

    /// <summary>对 from 指向的 array 做合计/聚合，得到标量</summary>
    public FlowAggregateDsl? Aggregate { get; set; }

    /// <summary>object → 单元素 array 时设为 array</summary>
    public string? Wrap { get; set; }

    /// <summary>
    /// 为 true 时：API 的 <c>data</c> 直接等于本字段值（通常为 array 或 object），
    /// 不再包成 <c>{ "字段名": ... }</c>。此时 finalOutputs 只能有这一项。
    /// 例：<c>{"success":true,"data":[...]}</c>
    /// </summary>
    public bool Promote { get; set; }

    /// <summary>
    /// 分组：扁平行 → 主表多字段 + 子表。
    /// by 可为字符串或字符串数组；header 主属性；children 子列表。
    /// </summary>
    public FlowOutputGroupDsl? Group { get; set; }
}

/// <summary>End 出参 list 映射</summary>
public class FlowOutputMapDsl
{
    public List<FlowOutputMapItemDsl>? Item { get; set; }
}

public class FlowOutputMapItemDsl
{
    public string Name { get; set; } = null!;

    public string Type { get; set; } = "string";

    /// <summary>
    /// 相对当前元素的路径（如 id）；带 input./sys./vars./results. 或引用名. 时走全局上下文。
    /// 也可 { "literal": ... } / { "template": "ORD-{{id}}" }
    /// </summary>
    public JsonNode? From { get; set; }

    public FlowVisibleToDsl? VisibleTo { get; set; }

    public bool Sensitive { get; set; }

    /// <summary>
    /// 嵌套投影（可无限层级）：
    /// type=array 时对 From 解析出的数组逐行按 map.item 投影；
    /// type=object 时对 From 解析出的对象按 map.item 投影字段。
    /// </summary>
    public FlowOutputMapDsl? Map { get; set; }
}

public class FlowAggregateDsl
{
    /// <summary>count | sum | avg | min | max | first | last</summary>
    public string Op { get; set; } = "count";

    /// <summary>相对每个元素的数值路径（count 可空）</summary>
    public string? Path { get; set; }
}

/// <summary>
/// 分组重组。示例：
/// by: "orderId" 或 ["orderId","wh"]
/// header: [{ name, from, take|aggregate }]
/// children: { name: "items", map: { item: [...] } }
/// promote: true 时（仅单组）把结果字段提升到 data 根级，不再包在出参名下
/// </summary>
public class FlowOutputGroupDsl
{
    /// <summary>分组键：JSON 字符串或字符串数组</summary>
    public JsonNode? By { get; set; }

    /// <summary>主表字段（默认每组 take=first）</summary>
    public List<FlowOutputGroupHeaderDsl>? Header { get; set; }

    /// <summary>子表明细</summary>
    public FlowOutputGroupChildrenDsl? Children { get; set; }

    /// <summary>
    /// 为 true 时：分组结果必须恰好 1 组，其 header/children 字段直接写入 data 根级
    ///（如 orderId、items），而不是 data.orders = [{ ... }]。
    /// </summary>
    public bool Promote { get; set; }
}

public class FlowOutputGroupHeaderDsl
{
    public string Name { get; set; } = null!;

    /// <summary>相对行内路径，默认等于 Name</summary>
    public string? From { get; set; }

    /// <summary>first | last（与 Aggregate 二选一，默认 first）</summary>
    public string? Take { get; set; }

    /// <summary>组内聚合，如 sum amount</summary>
    public FlowAggregateDsl? Aggregate { get; set; }

    public FlowVisibleToDsl? VisibleTo { get; set; }

    public bool Sensitive { get; set; }
}

public class FlowOutputGroupChildrenDsl
{
    public string Name { get; set; } = "items";

    public FlowOutputMapDsl? Map { get; set; }

    public FlowVisibleToDsl? VisibleTo { get; set; }
}

public class FlowVisibleToDsl
{
    /// <summary>all | roles | permissions | none</summary>
    public string Mode { get; set; } = "all";

    public List<string>? RoleNames { get; set; }

    public List<string>? RoleIds { get; set; }

    public List<string>? Permissions { get; set; }
}

public class FlowDslNode
{
    public string Id { get; set; } = null!;

    public string Type { get; set; } = null!;

    /// <summary>
    /// 节点引用名（短名）。下游用 setLevel.level 或短名 level，不必写内部 Id。
    /// </summary>
    public string? Ref { get; set; }

    /// <summary>Http | Code | Sql（当 Type=Logic 时）</summary>
    public string? Kind { get; set; }

    public bool Async { get; set; }

    public int? TimeoutMs { get; set; }

    /// <summary>
    /// 业务数据根（相对原始结果信封）。Http 常用 body.data；Sql 常用 rows。
    /// outputs.from 优先相对该根解析，找不到再回退 raw 根；statusCode / body.xxx 等信封路径始终相对 raw。
    /// </summary>
    public string? ResultRoot { get; set; }

    public List<FlowNodeBindingDsl>? Inputs { get; set; }

    public List<FlowNodeBindingDsl>? Outputs { get; set; }

    /// <summary>
    /// End 节点最终 API 出参契约（含 visibleTo / map / aggregate）。
    /// 优先于根级 outputs；根级 outputs 仅作兼容与定义摘要。
    /// </summary>
    public List<FlowOutputParameterDsl>? FinalOutputs { get; set; }

    /// <summary>HTTP 请求头映射（name → from）</summary>
    public List<FlowNodeBindingDsl>? Headers { get; set; }

    /// <summary>Mask 节点：rules | items</summary>
    /// <remarks>复用 Strategy 字段亦可；本字段优先。</remarks>
    public string? MaskStrategy { get; set; }

    /// <summary>Mask strategy=items 时，指向 inputs 中的 array 字段名</summary>
    public string? ItemField { get; set; }

    /// <summary>Mask 脱敏规则表</summary>
    public List<FlowMaskRuleDsl>? MaskRules { get; set; }

    /// <summary>
    /// HTTP 参数拼装：auto | query | json | form | raw。
    /// auto：GET/DELETE 拼 query，其余 JSON body。
    /// </summary>
    public string? BodyMode { get; set; }

    /// <summary>表达式为真时抛出业务异常（兼容；优先用 FailItems + FailCombine）</summary>
    public string? FailWhen { get; set; }

    public string? FailMessage { get; set; }

    public string? FailCode { get; set; }

    /// <summary>异常条件表（与 Condition 相同模型，可引用本节点出参短名 / input / sys）</summary>
    public List<FlowConditionItemDsl>? FailItems { get; set; }

    /// <summary>异常条件组合式，如 1 and (2 or 3)</summary>
    public string? FailCombine { get; set; }

    public FlowConditionBlockDsl? Entry { get; set; }

    /// <summary>Condition 节点共享条件表</summary>
    public List<FlowConditionItemDsl>? Items { get; set; }

    /// <summary>firstMatch | exclusive</summary>
    public string? Strategy { get; set; }

    // ---- 兼容旧节点字段 ----
    public string? Expression { get; set; }

    public string? Message { get; set; }

    public string? Name { get; set; }

    public string? Variable { get; set; }

    public JsonNode? Value { get; set; }

    public string? Method { get; set; }

    public string? Url { get; set; }

    public JsonNode? Body { get; set; }

    public string? ResponseVariable { get; set; }

    public string? Script { get; set; }

    /// <summary>Code/Sql 绑定的数据源编码（白名单）</summary>
    public string? DataSourceId { get; set; }

    /// <summary>兼容旧字段 dataSource</summary>
    public string? DataSource { get; set; }

    public string? Sql { get; set; }

    /// <summary>RabbitMqPublish：交换机名（广播默认 Meta.Dow.Orchestration.Broadcast）</summary>
    public string? Exchange { get; set; }

    /// <summary>fanout（广播）| topic | direct</summary>
    public string? ExchangeType { get; set; }

    /// <summary>topic/direct 路由键模板；fanout 可忽略</summary>
    public string? RoutingKey { get; set; }

    /// <summary>消息是否持久化</summary>
    public bool? Persistent { get; set; }

    /// <summary>fail | ignore</summary>
    public string? OnError { get; set; }

    /// <summary>消息体字段映射（局部，不进全局 vars）</summary>
    public FlowOutputMapDsl? Payload { get; set; }

    /// <summary>object | array | raw</summary>
    public string? PayloadMode { get; set; }

    /// <summary>payloadMode=raw 时取该路径作为整包 JSON</summary>
    public string? PayloadFrom { get; set; }

    /// <summary>SubFlow：被调用的已发布逻辑编码（flowKey）</summary>
    public string? SubFlowKey { get; set; }

    public string? ResolveDataSourceId()
    {
        if (!string.IsNullOrWhiteSpace(DataSourceId))
        {
            return DataSourceId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(DataSource))
        {
            return DataSource.Trim();
        }

        return null;
    }
}

public class FlowNodeBindingDsl
{
    public string Name { get; set; } = null!;

    public string? Type { get; set; }

    public bool Required { get; set; }

    /// <summary>
    /// 字符串路径（input.x / sys.Now / results.n.f / sys.Now - 3d）
    /// 或 JSON 对象 { "literal": ... }
    /// vars.x 仅兼容旧 DSL。
    /// </summary>
    public JsonNode? From { get; set; }

    /// <summary>
    /// 写入目标；默认为 results.{nodeId}.{name}。勿再默认写入全局 vars。
    /// </summary>
    public string? To { get; set; }

    public string? Transform { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// 当 From 解析为 array 时，按 map.item 投影每行（与 End.finalOutputs.map 同模型）。
    /// 用于 Http body.data.lines 等通用 list 转换，不必等到 End。
    /// </summary>
    public FlowOutputMapDsl? Map { get; set; }
}

public class FlowConditionBlockDsl
{
    public List<FlowConditionItemDsl> Items { get; set; } = [];

    public string? Combine { get; set; }
}

public class FlowConditionItemDsl
{
    public int No { get; set; }

    public string? Left { get; set; }

    public string? Op { get; set; }

    public JsonNode? Right { get; set; }
}

public class FlowDslEdge
{
    public string Source { get; set; } = null!;

    public string Target { get; set; } = null!;

    /// <summary>兼容旧 Condition：true / false</summary>
    public string? When { get; set; }

    /// <summary>条件组合式，如 1 and (2 or 3)</summary>
    public string? Combine { get; set; }

    public bool IsDefault { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FlowConditionItemDsl>? Items { get; set; }
}

/// <summary>
/// Mask 脱敏规则。op: fixed | keepEmpty | phone | idCard | email | bankCard | name | mask | hash | redact | drop
/// </summary>
public class FlowMaskRuleDsl
{
    public string Field { get; set; } = null!;

    public string Op { get; set; } = "fixed";

    public string? Value { get; set; }

    public int? KeepStart { get; set; }

    public int? KeepEnd { get; set; }

    public string? MaskChar { get; set; }

    /// <summary>hash 可选盐路径（相对对象或全局）</summary>
    public string? SaltFrom { get; set; }
}
