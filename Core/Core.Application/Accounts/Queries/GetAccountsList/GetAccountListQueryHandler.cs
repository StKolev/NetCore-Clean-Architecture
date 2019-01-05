﻿using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Domain.Entities;
using Core.Infrastructure.Configuration;
using Core.Infrastructure.Persistence.DocumentDatabase;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using System.Text;
using Microsoft.Azure.Documents.Linq;
using Core.Application.Accounts.Models.Views;
using Core.Application.Accounts.Models.Documents;
using Serilog;

namespace Core.Application.Accounts.Queries
{
    public class GetAccountListQueryHandler : IRequestHandler<GetAccountListQuery, AccountListResultsViewModel>
    {
        //MediatR will automatically inject  dependencies
        private readonly IMediator _mediator;
        private readonly ICoreConfiguration _coreConfiguration;
        private readonly IDocumentContext _documentContext;


        public GetAccountListQueryHandler(IDocumentContext documentContext, ICoreConfiguration coreConfiguration, IMediator mediator)
        {
            _mediator = mediator;
            _coreConfiguration = coreConfiguration;
            _documentContext = documentContext;

        }

        public async Task<AccountListResultsViewModel> Handle(GetAccountListQuery request, CancellationToken cancellationToken)
        {
            //-----------------------------------------------------
            // TODO: DocumentDB will soon have skip/take
            // For now we use continuation token
            // For more robust query capabilities use Azure Search via: SearchAccountsQuery
            //-----------------------------------------------------

            // Prepare our view model to be returned
            var accountsListViewModel = new AccountListResultsViewModel();

            // TODO: Check user role to include data for the view to use
            accountsListViewModel.DeleteEnabled = false;
            accountsListViewModel.EditEnabled = false;

            try
            {

                // Create the query
                var sqlQuery = new StringBuilder(String.Concat(
                    "SELECT d.id, d.Name, d.NameKey FROM Documents d ORDER BY d.",
                    request.OrderBy,
                    " ",
                    request.OrderDirection
                    ));

                var sqlSpec = new SqlQuerySpec { QueryText = sqlQuery.ToString() };

                // Generate collection uri
                Uri collectionUri = UriFactory.CreateDocumentCollectionUri(_documentContext.Settings.Database, _documentContext.Settings.Collection);

                // Generate FeedOptions/ParitionKey
                var feedOptions = new FeedOptions
                {
                    PartitionKey = new PartitionKey(Common.Constants.DocumentType.Account()),
                    MaxItemCount = request.PageSize, //<-- This is the page size
                    RequestContinuation = request.ContinuationToken
                };

                // Run query against the document store
                var query = _documentContext.Client.CreateDocumentQuery<AccountDocumentModel>(
                    collectionUri,
                    sqlSpec,
                    feedOptions
                ).AsDocumentQuery(); //<-- 'AsDocumentQuery' extension method casts the IOrderedQueryable query to an IDocumentQuery

                var result = await query.ExecuteNextAsync<AccountDocumentModel>(); //<-- Get the first page of results as AccountDocumentModel(s)

                if (query.HasMoreResults)
                {
                    //If there are more results pass back a continuation token so the caller can get the next batch
                    accountsListViewModel.HasMoreResults = true;
                    accountsListViewModel.ContinuationToken = result.ResponseContinuation;
                }

                
                if(result != null && result.Count > 0)
                {
                    accountsListViewModel.Count = result.Count;

                    foreach (var accountDocument in result)
                    {
                        //Use AutoMapper to transform DocumentModel into Domain Model (Configure via Core.Startup.AutoMapperConfiguration)
                        var account = AutoMapper.Mapper.Map<AccountListViewItem>(accountDocument);
                        accountsListViewModel.Accounts.Add(account);
                    }
                }

                return accountsListViewModel;
            }
            catch(Exception e)
            {
                // Log our exception.
                // Use structured logging to capture the full exception object.
                // Serilog provides the @ destructuring operator to help preserve object structure for our logs.
                Log.Error("Error: {@e}", e);

                return null;
            }

        }
    }
}
