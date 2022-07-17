using Microsoft.Extensions.Logging;
using Migration.Toolkit.Core.Abstractions;
using Migration.Toolkit.Core.Contexts;
using Migration.Toolkit.Core.MigrationProtocol;

namespace Migration.Toolkit.Core.Mappers;

using Migration.Toolkit.KXP.Models;

public class OmContactGroupMapper : EntityMapperBase<KX13.Models.OmContactGroup, OmContactGroup>
{
    public OmContactGroupMapper(
        ILogger<OmContactGroupMapper> logger,
        PrimaryKeyMappingContext primaryKeyMappingContext,
        IMigrationProtocol protocol
    ) : base(logger, primaryKeyMappingContext, protocol)
    {
    }

    protected override OmContactGroup? CreateNewInstance(KX13.Models.OmContactGroup tSourceEntity, MappingHelper mappingHelper, AddFailure addFailure) => new();

    protected override OmContactGroup MapInternal(KX13.Models.OmContactGroup source, OmContactGroup target, bool newInstance,
        MappingHelper mappingHelper, AddFailure addFailure)
    {
        target.ContactGroupName = source.ContactGroupName;
        target.ContactGroupDisplayName = source.ContactGroupDisplayName;
        target.ContactGroupDescription = source.ContactGroupDescription;
        target.ContactGroupDynamicCondition = source.ContactGroupDynamicCondition;
        target.ContactGroupEnabled = source.ContactGroupEnabled;
        target.ContactGroupLastModified = source.ContactGroupLastModified;
        target.ContactGroupGuid = source.ContactGroupGuid;
        target.ContactGroupStatus = source.ContactGroupStatus;

        return target;
    }
}