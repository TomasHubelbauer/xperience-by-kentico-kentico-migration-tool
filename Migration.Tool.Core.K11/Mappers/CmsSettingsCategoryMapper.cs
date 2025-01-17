using Microsoft.Extensions.Logging;

using Migration.Tool.Common.Abstractions;
using Migration.Tool.Common.MigrationProtocol;
using Migration.Tool.Core.K11.Contexts;
using Migration.Tool.K11.Models;

namespace Migration.Tool.Core.K11.Mappers;

public class CmsSettingsCategoryMapper(
    ILogger<CmsSettingsCategoryMapper> logger,
    PrimaryKeyMappingContext pkContext,
    IProtocol protocol,
    IEntityMapper<CmsResource, KXP.Models.CmsResource> cmsResourceMapper)
    : EntityMapperBase<CmsSettingsCategory,
        KXP.Models.CmsSettingsCategory>(logger, pkContext, protocol)
{
    protected override KXP.Models.CmsSettingsCategory? CreateNewInstance(CmsSettingsCategory source, MappingHelper mappingHelper,
        AddFailure addFailure) => new();


    protected override KXP.Models.CmsSettingsCategory MapInternal(CmsSettingsCategory source, KXP.Models.CmsSettingsCategory target, bool newInstance, MappingHelper mappingHelper, AddFailure addFailure)
    {
        // no category guid to match on...
        if (newInstance)
        {
            target.CategoryOrder = source.CategoryOrder;
            target.CategoryName = source.CategoryName;
            target.CategoryDisplayName = source.CategoryDisplayName;
            target.CategoryIdpath = source.CategoryIdpath;
            target.CategoryLevel = source.CategoryLevel;
            target.CategoryChildCount = source.CategoryChildCount;
            target.CategoryIconPath = source.CategoryIconPath;
            target.CategoryIsGroup = source.CategoryIsGroup;
            target.CategoryIsCustom = source.CategoryIsCustom;
        }

        if (source.CategoryResource != null)
        {
            if (target.CategoryResource != null && source.CategoryResourceId != null && target.CategoryResourceId != null)
            {
                // skip if target is present
                logger.LogTrace("Skipping category resource '{ResourceGuid}', already present in target instance", target.CategoryResource.ResourceGuid);
                pkContext.SetMapping<CmsResource>(r => r.ResourceId, source.CategoryResourceId.Value, target.CategoryResourceId.Value);
            }
            else
            {
                switch (cmsResourceMapper.Map(source.CategoryResource, target.CategoryResource))
                {
                    case { Success: true } result:
                    {
                        target.CategoryResource = result.Item;
                        break;
                    }
                    case { Success: false } result:
                    {
                        addFailure(new MapperResultFailure<KXP.Models.CmsSettingsCategory>(result.HandbookReference));
                        break;
                    }

                    default:
                        break;
                }
            }
        }
        else if (mappingHelper.TranslateIdAllowNulls<CmsResource>(r => r.ResourceId, source.CategoryResourceId, out int? categoryResourceId))
        {
            target.CategoryResourceId = categoryResourceId;
        }

        if (source.CategoryParent != null)
        {
            switch (Map(source.CategoryParent, target.CategoryParent))
            {
                case { Success: true } result:
                {
                    target.CategoryParent = result.Item;
                    break;
                }
                case { Success: false } result:
                {
                    addFailure(new MapperResultFailure<KXP.Models.CmsSettingsCategory>(result.HandbookReference));
                    break;
                }

                default:
                    break;
            }
        }
        else if (mappingHelper.TranslateIdAllowNulls<CmsCategory>(c => c.CategoryId, source.CategoryParentId, out int? categoryParentId))
        {
            target.CategoryParentId = categoryParentId;
        }

        return target;
    }
}
