﻿namespace Migration.Toolkit.Core.Handlers;

using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Migration.Toolkit.Core.Abstractions;
using Migration.Toolkit.Core.Contexts;
using Migration.Toolkit.Core.MigrationProtocol;
using Migration.Toolkit.KX13.Context;
using Migration.Toolkit.KX13.Models;
using Migration.Toolkit.KXP.Context;

public class MigrateDataProtectionCommandHandler : IRequestHandler<MigrateDataProtectionCommand, CommandResult>, IDisposable
{
    private readonly ILogger<MigrateDataProtectionCommandHandler> _logger;
    private readonly IDbContextFactory<KxpContext> _kxpContextFactory;
    private readonly IDbContextFactory<KX13Context> _kx13ContextFactory;
    private readonly IEntityMapper<CmsConsent, KXP.Models.CmsConsent> _consentMapper;
    private readonly IEntityMapper<CmsConsentArchive, KXP.Models.CmsConsentArchive> _consentArchiveMapper;
    private readonly IEntityMapper<CmsConsentAgreement, KXP.Models.CmsConsentAgreement> _consentAgreementMapper;
    private readonly PrimaryKeyMappingContext _primaryKeyMappingContext;
    private readonly IMigrationProtocol _migrationProtocol;

    private KxpContext _kxpContext;

    private static readonly int _batchSize = 1000;

    public MigrateDataProtectionCommandHandler(
        ILogger<MigrateDataProtectionCommandHandler> logger,
        IDbContextFactory<KxpContext> kxpContextFactory,
        IDbContextFactory<KX13Context> kx13ContextFactory,
        IEntityMapper<CmsConsent, KXP.Models.CmsConsent> consentMapper,
        IEntityMapper<CmsConsentArchive, KXP.Models.CmsConsentArchive> consentArchiveMapper,
        IEntityMapper<CmsConsentAgreement, KXP.Models.CmsConsentAgreement> consentAgreementMapper,
        PrimaryKeyMappingContext primaryKeyMappingContext,
        IMigrationProtocol migrationProtocol
    )
    {
        _logger = logger;
        _kxpContextFactory = kxpContextFactory;
        _kx13ContextFactory = kx13ContextFactory;
        _consentMapper = consentMapper;
        _consentArchiveMapper = consentArchiveMapper;
        _consentAgreementMapper = consentAgreementMapper;
        _primaryKeyMappingContext = primaryKeyMappingContext;
        _migrationProtocol = migrationProtocol;
        _kxpContext = _kxpContextFactory.CreateDbContext();
    }
    
    public async Task<CommandResult> Handle(MigrateDataProtectionCommand request, CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(request.BatchSize ?? _batchSize, 5);
        
        await MigrateConsent(cancellationToken);
        await MigrateConsentArchive(cancellationToken);
        await MigrateConsentAgreement(cancellationToken, batchSize);

        return new GenericCommandResult();
    }

    private async Task<CommandResult> MigrateConsent(CancellationToken cancellationToken)
    {
        await using var kx13Context = await _kx13ContextFactory.CreateDbContextAsync(cancellationToken);
        
        foreach (var kx13Consent in kx13Context.CmsConsents)
        {
            _migrationProtocol.FetchedSource(kx13Consent);
            _logger.LogTrace("Migrating consent {ConsentName} with ConsentGuid {ConsentGuid}", kx13Consent.ConsentName, kx13Consent.ConsentGuid);
            
            var kxoConsent = await _kxpContext.CmsConsents.FirstOrDefaultAsync(consent => consent.ConsentGuid == kx13Consent.ConsentGuid, cancellationToken);
            _migrationProtocol.FetchedTarget(kxoConsent);

            var mapped = _consentMapper.Map(kx13Consent, kxoConsent);
            _migrationProtocol.MappedTarget(mapped);

            if (mapped is { Success : true } result)
            {
                var (cmsConsent, newInstance) = result;
                ArgumentNullException.ThrowIfNull(cmsConsent, nameof(cmsConsent));

                if (newInstance)
                {
                    _kxpContext.CmsConsents.Add(cmsConsent);
                }
                else
                {
                    _kxpContext.CmsConsents.Update(cmsConsent);
                }

                try
                {
                    await _kxpContext.SaveChangesAsync(cancellationToken);

                    _migrationProtocol.Success(kx13Consent, cmsConsent, mapped);
                    _logger.LogEntitySetAction(newInstance, cmsConsent);
                    _primaryKeyMappingContext.SetMapping<CmsConsent>(r => r.ConsentId, kx13Consent.ConsentId, cmsConsent.ConsentId);
                }
                /*Violation in unique index or Violation in unique constraint */
                catch (DbUpdateException dbUpdateException) when (dbUpdateException.InnerException is SqlException { Number: 2601 or 2627 } sqlException)
                {
                    _logger.LogEntitySetError(sqlException, newInstance, kx13Consent);
                    _migrationProtocol.Append(HandbookReferences
                        .DbConstraintBroken(sqlException, kx13Consent)
                        .WithMessage("Failed to migrate consent, target database constraint broken.")
                    );

                    await _kxpContext.DisposeAsync();
                    _kxpContext = await _kxpContextFactory.CreateDbContextAsync(cancellationToken);
                }
            }
        }

        return new GenericCommandResult();
    }

    private async Task<CommandResult> MigrateConsentArchive(CancellationToken cancellationToken)
    {
        await using var kx13Context = await _kx13ContextFactory.CreateDbContextAsync(cancellationToken);

        foreach (var kx13ArchiveConsent in kx13Context.CmsConsentArchives)
        {
            _migrationProtocol.FetchedSource(kx13ArchiveConsent);
            _logger.LogTrace("Migrating consent archive with ConsentArchiveGuid {ConsentGuid}", kx13ArchiveConsent.ConsentArchiveGuid);

            var kxoConsentArchive = await _kxpContext.CmsConsentArchives.FirstOrDefaultAsync(consentArchive => consentArchive.ConsentArchiveGuid == kx13ArchiveConsent.ConsentArchiveGuid, cancellationToken);
            _migrationProtocol.FetchedTarget(kxoConsentArchive);

            var mapped = _consentArchiveMapper.Map(kx13ArchiveConsent, kxoConsentArchive);
            _migrationProtocol.MappedTarget(mapped);

            if (mapped is { Success : true } result)
            {
                var (cmsConsentArchive, newInstance) = result;
                ArgumentNullException.ThrowIfNull(cmsConsentArchive, nameof(cmsConsentArchive));

                if (newInstance)
                {
                    _kxpContext.CmsConsentArchives.Add(cmsConsentArchive);
                }
                else
                {
                    _kxpContext.CmsConsentArchives.Update(cmsConsentArchive);
                }

                try
                {
                    await _kxpContext.SaveChangesAsync(cancellationToken);

                    _migrationProtocol.Success(kx13ArchiveConsent, cmsConsentArchive, mapped);
                    _logger.LogEntitySetAction(newInstance, cmsConsentArchive);
                    _primaryKeyMappingContext.SetMapping<CmsConsentArchive>(r => r.ConsentArchiveGuid,
                        kx13ArchiveConsent.ConsentArchiveId, cmsConsentArchive.ConsentArchiveId);
                }
                /*Violation in unique index or Violation in unique constraint */
                catch (DbUpdateException dbUpdateException) when (dbUpdateException.InnerException is SqlException { Number: 2601 or 2627 } sqlException)
                {
                    _logger.LogEntitySetError(sqlException, newInstance, kx13ArchiveConsent);
                    _migrationProtocol.Append(HandbookReferences
                        .DbConstraintBroken(sqlException, kx13ArchiveConsent)
                        .WithMessage("Failed to migrate consent archive, target database constraint broken.")
                    );

                    await _kxpContext.DisposeAsync();
                    _kxpContext = await _kxpContextFactory.CreateDbContextAsync(cancellationToken);
                }
            }
        }

        return new GenericCommandResult();
    }

    private async Task<CommandResult> MigrateConsentAgreement(CancellationToken cancellationToken, int batchSize)
    {
        await using var kx13Context = await _kx13ContextFactory.CreateDbContextAsync(cancellationToken);
        var index = 0;
        var indexFull = 0;
        var consentAgreementUpdates= new List<KXP.Models.CmsConsentAgreement>();
        var consentAgreementNews = new List<KXP.Models.CmsConsentAgreement>();
        var itemsCount = kx13Context.CmsConsentAgreements.Count();

        foreach (var kx13ConsentAgreement in kx13Context.CmsConsentAgreements)
        {
            _migrationProtocol.FetchedSource(kx13ConsentAgreement);
            _logger.LogTrace("Migrating consent agreement with ConsentAgreementGuid {ConsentAgreementGuid}", kx13ConsentAgreement.ConsentAgreementGuid);

            var kxoConsentAgreement = await _kxpContext.CmsConsentAgreements.FirstOrDefaultAsync(consentAgreement => consentAgreement.ConsentAgreementGuid == kx13ConsentAgreement.ConsentAgreementGuid, cancellationToken);
            _migrationProtocol.FetchedTarget(kxoConsentAgreement);

            var mapped = _consentAgreementMapper.Map(kx13ConsentAgreement, kxoConsentAgreement);
            _migrationProtocol.MappedTarget(mapped);

            if (mapped is { Success : true } result)
            {
                var (cmsConsentAgreement, newInstance) = result;
                ArgumentNullException.ThrowIfNull(cmsConsentAgreement, nameof(cmsConsentAgreement));

                if (newInstance)
                {
                    consentAgreementNews.Add(cmsConsentAgreement);
                }
                else
                {
                    consentAgreementUpdates.Add(cmsConsentAgreement);
                }
            }

            index++;
            indexFull++;

            if (index == batchSize || indexFull == itemsCount)
            {
                _kxpContext.CmsConsentAgreements.AddRange(consentAgreementNews);
                _kxpContext.CmsConsentAgreements.UpdateRange(consentAgreementUpdates);

                try
                {
                    await _kxpContext.SaveChangesAsync(cancellationToken);

                    foreach (var newKx13ConsentAgreement in consentAgreementNews)
                    {
                        _migrationProtocol.Success(kx13ConsentAgreement, newKx13ConsentAgreement, mapped);
                        _logger.LogInformation("CmsConsentAgreement: with ConsentAgreementGuid \'ConsentAgreementGuid}\' was inserted",
                            newKx13ConsentAgreement.ConsentAgreementGuid);
                    }

                    foreach (var updateKx13ConsentAgreement in consentAgreementUpdates)
                    {
                        _migrationProtocol.Success(kx13ConsentAgreement, updateKx13ConsentAgreement, mapped);
                        _logger.LogInformation("CmsConsentAgreement: with ConsentAgreementGuid \'ConsentAgreementGuid}\' was updated",
                            updateKx13ConsentAgreement.ConsentAgreementGuid);
                    }
                }
                catch (DbUpdateException dbUpdateException) when (
                    dbUpdateException.InnerException is SqlException sqlException &&
                    sqlException.Message.Contains("Cannot insert duplicate key row in object")
                )
                {
                    await _kxpContext.DisposeAsync();

                    _migrationProtocol.Append(HandbookReferences
                        .ErrorCreatingTargetInstance<KXP.Models.CmsConsentAgreement>(dbUpdateException)
                        .NeedsManualAction()
                        .WithIdentityPrints(consentAgreementNews)
                    );
                    _logger.LogEntitiesSetError(dbUpdateException, true, consentAgreementNews);


                    _migrationProtocol.Append(HandbookReferences
                        .ErrorUpdatingTargetInstance<KXP.Models.CmsConsentAgreement>(dbUpdateException)
                        .NeedsManualAction()
                        .WithIdentityPrints(consentAgreementUpdates)
                    );
                    
                    _logger.LogEntitiesSetError(dbUpdateException, false, consentAgreementUpdates);

                    _kxpContext = await _kxpContextFactory.CreateDbContextAsync(cancellationToken);
                }
                finally
                {
                    index = 0;
                    consentAgreementUpdates = new List<KXP.Models.CmsConsentAgreement>();
                    consentAgreementNews = new List<KXP.Models.CmsConsentAgreement>();
                }
            }
        }

        return new GenericCommandResult();
    }
    
    public void Dispose()
    {
        _kxpContext.Dispose();
    }
}