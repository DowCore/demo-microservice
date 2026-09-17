using AutoMapper;
using Meta.Dow.SaaS.Orchestration;

namespace Meta.Dow.SaaS;

public class SaaSApplicationAutoMapperProfile : Profile
{
    public SaaSApplicationAutoMapperProfile()
    {
        CreateMap<FlowDefinition, FlowDefinitionDto>();
        CreateMap<FlowVersion, FlowVersionDto>();
        CreateMap<FlowInstance, FlowInstanceDto>();
        CreateMap<NodeExecutionRecord, NodeExecutionDto>();
    }
}
