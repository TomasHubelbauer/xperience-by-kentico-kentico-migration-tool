﻿namespace Migration.Toolkit.Core.Handlers;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Xml.Linq;
using CMS.DataEngine;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Migration.Toolkit.Common;
using Migration.Toolkit.Core.Abstractions;
using Migration.Toolkit.Core.Contexts;
using Migration.Toolkit.Core.MigrationProtocol;
using Migration.Toolkit.Core.Services.BulkCopy;
using Migration.Toolkit.KX13.Context;
using Migration.Toolkit.KX13.Models;
using Migration.Toolkit.KXP.Api;
using Migration.Toolkit.KXP.Context;

public class MigrateFormsCommandHandler : IRequestHandler<MigrateFormsCommand, CommandResult>, IDisposable
{
    private readonly ILogger<MigrateFormsCommandHandler> _logger;
    private readonly IDbContextFactory<KxpContext> _kxpContextFactory;
    private readonly IDbContextFactory<KX13Context> _kx13ContextFactory;
    private readonly IEntityMapper<CmsClass, DataClassInfo> _dataClassMapper;
    private readonly IEntityMapper<CmsForm, KXP.Models.CmsForm> _cmsFormMapper;
    private readonly KxpClassFacade _kxpClassFacade;
    private readonly BulkDataCopyService _bulkDataCopyService;
    private readonly ToolkitConfiguration _toolkitConfiguration;
    private readonly PrimaryKeyMappingContext _primaryKeyMappingContext;
    private readonly IMigrationProtocol _migrationProtocol;

    private KxpContext _kxpContext;

    public MigrateFormsCommandHandler(
        ILogger<MigrateFormsCommandHandler> logger,
        IDbContextFactory<KxpContext> kxpContextFactory,
        IDbContextFactory<KX13Context> kx13ContextFactory,
        IEntityMapper<CmsClass, DataClassInfo> dataClassMapper,
        IEntityMapper<CmsForm, KXP.Models.CmsForm> cmsFormMapper,
        KxpClassFacade kxpClassFacade,
        BulkDataCopyService bulkDataCopyService,
        ToolkitConfiguration toolkitConfiguration,
        PrimaryKeyMappingContext primaryKeyMappingContext,
        IMigrationProtocol migrationProtocol
    )
    {
        _logger = logger;
        _kxpContextFactory = kxpContextFactory;
        _kx13ContextFactory = kx13ContextFactory;
        _dataClassMapper = dataClassMapper;
        _cmsFormMapper = cmsFormMapper;
        _kxpClassFacade = kxpClassFacade;
        _bulkDataCopyService = bulkDataCopyService;
        _toolkitConfiguration = toolkitConfiguration;
        _primaryKeyMappingContext = primaryKeyMappingContext;
        _migrationProtocol = migrationProtocol;
        _kxpContext = kxpContextFactory.CreateDbContext();
    }

    public async Task<CommandResult> Handle(MigrateFormsCommand request, CancellationToken cancellationToken)
    {
        var migratedSiteIds = _toolkitConfiguration.RequireExplicitMapping<CmsSite>(s => s.SiteId).Keys.ToList();

        await using var kx13Context = await _kx13ContextFactory.CreateDbContextAsync(cancellationToken);

        var cmsClassForms = kx13Context.CmsClasses
            .Include(c => c.CmsForms)
            .Where(x => x.ClassIsForm == true)
            .OrderBy(x => x.ClassId)
            .AsEnumerable();

        foreach (var kx13Class in cmsClassForms)
        {
            _migrationProtocol.FetchedSource(kx13Class);

            // checking of kx13Class.ClassConnectionString is not necessary

            if (!kx13Class.CmsForms.Any(f => migratedSiteIds.Contains(f.FormSiteId)))
            {
                _logger.LogWarning("CmsClass: {ClassName} => Class site is not migrated => skipping", kx13Class.ClassName);
                continue;
            }

            var kxoDataClass = _kxpClassFacade.GetClass(kx13Class.ClassGuid);
            _migrationProtocol.FetchedTarget(kxoDataClass);

            var classSuccessFullySaved = MapAndSaveUsingKxoApi(kx13Class, kxoDataClass);
            if (!classSuccessFullySaved)
            {
                continue;
            }
            
            foreach (var kx13CmsForm in kx13Class.CmsForms)
            {
                _migrationProtocol.FetchedSource(kx13CmsForm);
                
                var kxoCmsForm = _kxpContext.CmsForms.FirstOrDefault(f => f.FormGuid == kx13CmsForm.FormGuid);
                
                _migrationProtocol.FetchedTarget(kxoCmsForm);

                var mapped = _cmsFormMapper.Map(kx13CmsForm, kxoCmsForm);
                _migrationProtocol.MappedTarget(mapped);

                if (mapped is { Success : true } result)
                {
                    var (cmsForm, newInstance) = result;
                    ArgumentNullException.ThrowIfNull(cmsForm, nameof(cmsForm));

                    try
                    {
                        if (newInstance)
                        {
                            _kxpContext.CmsForms.Add(cmsForm);
                        }
                        else
                        {
                            _kxpContext.CmsForms.Update(cmsForm);
                        }

                        await _kxpContext.SaveChangesAsync(cancellationToken);
                        _logger.LogEntitySetAction(newInstance, cmsForm);

                        _primaryKeyMappingContext.SetMapping<CmsForm>(
                            r => r.FormId,
                            kx13Class.ClassId,
                            cmsForm.FormId
                        );
                    }
                    catch (Exception ex)
                    {
                        await _kxpContext.DisposeAsync(); // reset context errors
                        _kxpContext = await _kxpContextFactory.CreateDbContextAsync(cancellationToken);

                        _migrationProtocol.Append(HandbookReferences
                            .ErrorCreatingTargetInstance<KXP.Models.CmsForm>(ex)
                            .NeedsManualAction()
                            .WithIdentityPrint(cmsForm)
                        );
                        _logger.LogEntitySetError(ex, newInstance, cmsForm);
                        
                        continue;
                    }
                }

                Debug.Assert(kx13Class.ClassTableName != null, "kx13Class.ClassTableName != null");
                // var csi = new ClassStructureInfo(kx13Class.ClassXmlSchema, kx13Class.ClassXmlSchema, kx13Class.ClassTableName);
                
                XNamespace nsSchema = "http://www.w3.org/2001/XMLSchema";
                XNamespace msSchema = "urn:schemas-microsoft-com:xml-msdata";
                var xDoc = XDocument.Parse(kx13Class.ClassXmlSchema);
                var autoIncrementColumns = xDoc.Descendants(nsSchema + "element")
                    .Where(x => x.Attribute(msSchema + "AutoIncrement")?.Value == "true")
                    .Select(x => x.Attribute("name")?.Value).ToImmutableHashSet();

                Debug.Assert(autoIncrementColumns.Count == 1, "autoIncrementColumns.Count == 1");
                // TODO tk: 2022-07-08 not true : autoIncrementColumns.First() == csi.IDColumn
                // Debug.Assert(autoIncrementColumns.First() == csi.IDColumn, "autoIncrementColumns.First() == csi.IDColumn");

                var r = (kx13Class.ClassTableName, kx13Class.ClassGuid, autoIncrementColumns);
                _logger.LogTrace("Class '{ClassGuild}' Resolved as: {Result}", kx13Class.ClassGuid, r);
                
                // check if data is present in target tables
                if (_bulkDataCopyService.CheckIfDataExistsInTargetTable(kx13Class.ClassTableName))
                {
                    _logger.LogWarning("Data exists in target coupled data table '{TableName}' - cannot migrate, skipping form data migration", r.ClassTableName);
                    _migrationProtocol.Append(HandbookReferences
                        .DataMustNotExistInTargetInstanceTable(kx13Class.ClassTableName)
                    );
                    continue;
                }

                var bulkCopyRequest = new BulkCopyRequest(
                    kx13Class.ClassTableName, s => !autoIncrementColumns.Contains(s), _ => true,
                    20000
                );

                _logger.LogTrace("Bulk data copy request: {Request}", bulkCopyRequest);
                _bulkDataCopyService.CopyTableToTable(bulkCopyRequest);
            }
        }

        return new GenericCommandResult();
    }

    private bool MapAndSaveUsingKxoApi(CmsClass kx13Class, DataClassInfo kxoDataClass)
    {
        var mapped = _dataClassMapper.Map(kx13Class, kxoDataClass);
        _migrationProtocol.MappedTarget(mapped);

        if (mapped is { Success : true } result)
        {
            var (dataClassInfo, newInstance) = result;
            ArgumentNullException.ThrowIfNull(dataClassInfo, nameof(dataClassInfo));

            try
            {
                _kxpClassFacade.SetClass(dataClassInfo);

                _migrationProtocol.Success(kx13Class, dataClassInfo, mapped);
                _logger.LogEntitySetAction(newInstance, dataClassInfo);

                _primaryKeyMappingContext.SetMapping<CmsClass>(
                    r => r.ClassId,
                    kx13Class.ClassId,
                    dataClassInfo.ClassID
                );

                return true;
            }
            catch (Exception ex)
            {
                _migrationProtocol.Append(HandbookReferences
                    .ErrorCreatingTargetInstance<DataClassInfo>(ex)
                    .NeedsManualAction()
                    .WithIdentityPrint(dataClassInfo)
                );
                _logger.LogEntitySetError(ex, newInstance, dataClassInfo);
            }
        }
        
        return false;
    }

    public void Dispose()
    {
        _kxpContext.Dispose();
    }
}