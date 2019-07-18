﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Eshopworld.Caching.Cosmos;
using Eshopworld.Caching.Cosmos.Tests;
using Eshopworld.Tests.Core;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Xunit;

// ReSharper disable once CheckNamespace
public class CosmosCacheFactoryTests
{
    [Fact, IsUnit]
    public void Create_WithDocumentDirectAndPrimitiveType_RaisesException()
    {
        // Arrange
        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName, new CosmosCacheFactorySettings() { InsertMode = CosmosCache.InsertMode.Document }))
        {
            // Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create<string>(""));
        }
    }

    [Fact, IsIntegration]
    public void NewInstance_WithValidConnectionString_NoException()
    {
        // Arrange
        // Act
        using (new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName)) ;
        // Assert
    }

    [Fact, IsIntegration]
    public void Create_CosmosCache_NoException()
    {
        // Arrange
        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName))
        {
            // Act
            var instance = factory.Create<SimpleObject>("testCache");

            // Assert
            Assert.IsType<CosmosCache<SimpleObject>>(instance);
        }
    }

    [Fact, IsIntegration]
    public void Create_CosmosCacheMultipleTimes_NoException()
    {
        // Arrange
        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName))
        {
            // Act
            for (int i = 0; i < 10; i++)
            {
                factory.Create<SimpleObject>("testCache");
            }

            // Assert
            // should not throw
        }
    }

    [Fact, IsIntegration]
    public void Create_WithNonExistingCollection_NewCollectionIsCreated()
    {
        var tempCollectionName = Guid.NewGuid().ToString();

        // Arrange
        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName))
        {
            // Act
            factory.Create<SimpleObject>(tempCollectionName);

            Assert.Equal(System.Net.HttpStatusCode.OK, factory.DocumentClient.ReadDocumentCollectionAsync(new Uri($"dbs/test-db/colls/{tempCollectionName}", UriKind.Relative)).GetAwaiter().GetResult().StatusCode);
        }
    }

    [Fact, IsIntegration]
    public async Task Create_WithCustomIndexingPolicySettingsForCollection_NewCollectionIsCreated()
    {
        // Arrange
        var tempCollectionName = Guid.NewGuid().ToString();

        var settings = new CosmosCacheFactorySettings
        {
            IndexingSettings = new CosmosCacheFactoryIndexingSettings
            {
                ExcludedPaths = new[] { "/*" },
                IncludedPaths = new[] { $"/{nameof(SimpleObject.Foo)}/?", $"/{nameof(SimpleObject.Value)}/?" }
            }
        };

        var collectionUri = UriFactory.CreateDocumentCollectionUri(LocalClusterCosmosDb.DbName, tempCollectionName);

        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName, settings))
        using (var client = new DocumentClient(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey))
        {
            // Act
            factory.Create<SimpleObject>(tempCollectionName);

            // Assert
            var actualIndexingPolicy = (await client.ReadDocumentCollectionAsync(collectionUri))
                .Resource.IndexingPolicy;

            Assert.Single(actualIndexingPolicy.ExcludedPaths);
            Assert.Contains(actualIndexingPolicy.ExcludedPaths, x => x.Path == "/*");

            Assert.Equal(2, actualIndexingPolicy.IncludedPaths.Count);
            Assert.Contains(actualIndexingPolicy.IncludedPaths, x => x.Path == "/Foo/?");
            Assert.Contains(actualIndexingPolicy.IncludedPaths, x => x.Path == "/Value/?");

            // Cleanup
            await factory.DocumentClient.DeleteDocumentCollectionAsync(collectionUri);
        }
    }

    [Fact, IsIntegration]
    public async Task Create_WithEmptyIndexingPolicySettingsForCollection_CollectionIsCreatedWithDefaultPolicy()
    {
        // Arrange
        var tempCollectionName = Guid.NewGuid().ToString();
        var collectionUri = UriFactory.CreateDocumentCollectionUri(LocalClusterCosmosDb.DbName, tempCollectionName);

        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName))
        using (var client = new DocumentClient(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey))
        {
            // Act
            factory.Create<SimpleObject>(tempCollectionName);

            // Assert
            var actualIndexingPolicy = (await client.ReadDocumentCollectionAsync(collectionUri))
                .Resource.IndexingPolicy;

            Assert.Empty(actualIndexingPolicy.ExcludedPaths);

            Assert.Single(actualIndexingPolicy.IncludedPaths);
            Assert.Contains(actualIndexingPolicy.IncludedPaths, x => x.Path == "/*");

            // Cleanup
            await factory.DocumentClient.DeleteDocumentCollectionAsync(collectionUri);
        }
    }

    [Fact, IsIntegration]
    public async Task Create_WithDBSharedRUSetting_WithoutCollectionRUvalue_NoCollectionOfferOnlyDbOffer()
    {
        // Arrange
        var tempCollectionName = Guid.NewGuid().ToString();
        var tempDbName = Guid.NewGuid().ToString();
        var collectionUri = UriFactory.CreateDocumentCollectionUri(tempDbName, tempCollectionName);
        var databaseUri = UriFactory.CreateDatabaseUri(tempDbName);
        var offerThroughput = 500;
        var cosmosCacheFactorySettings = new CosmosCacheFactorySettings
        {
            DatabaseSharedRUs = true,
            UseKeyAsPartitionKey = true
        };

        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, tempDbName, cosmosCacheFactorySettings))
        using (var client = new DocumentClient(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey))
        {
            // Act
            await client.CreateDatabaseAsync(new Database { Id = tempDbName }, new RequestOptions
            {
                OfferThroughput = offerThroughput //non-default
            });
            
            //ensure temp collection is created
            factory.Create<SimpleObject>(tempCollectionName);

            Resource collectionResource = await client.ReadDocumentCollectionAsync(collectionUri);
            Resource databaseResource = await client.ReadDatabaseAsync(databaseUri);
            var collectionOffer = GetOffer(collectionResource, client);
            var databaseOffer = GetOffer(databaseResource, client);            
            var dbOfferResource = await client.ReadOfferAsync(databaseOffer.SelfLink);
            var dbOfferContent = dbOfferResource.Resource.GetPropertyValue<OfferContentV2>("content");

            // Assert
            Assert.Null(collectionOffer);
            Assert.Equal(offerThroughput, dbOfferContent.OfferThroughput);

            // Cleanup
            await factory.DocumentClient.DeleteDocumentCollectionAsync(collectionUri);            
            await factory.DocumentClient.DeleteDatabaseAsync(databaseUri);
        }
    }


    [Fact, IsIntegration]
    public async Task Create_WithDBSharedRUSetting_WithCollectionRUvalue_CollectionOfferAndNoDbOffer()
    {
        // Arrange
        var tempCollectionName = Guid.NewGuid().ToString();
        var tempDbName = Guid.NewGuid().ToString();
        var collectionUri = UriFactory.CreateDocumentCollectionUri(tempDbName, tempCollectionName);
        var databaseUri = UriFactory.CreateDatabaseUri(tempDbName);
        var dbOfferThroughput = 600;
        var cosmosCacheFactorySettings = new CosmosCacheFactorySettings
        {
            UseKeyAsPartitionKey = true,
            NewCollectionDefaultDTU = 500 //just so its different than default
        };

        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, tempDbName, cosmosCacheFactorySettings))
        using (var client = new DocumentClient(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey))
        {
            // Act
            await client.CreateDatabaseAsync(new Database { Id = tempDbName }, new RequestOptions
            {
                OfferThroughput = dbOfferThroughput //non-default
            });

            //ensure temp collection is created
            factory.Create<SimpleObject>(tempCollectionName);

            Resource collectionResource = await client.ReadDocumentCollectionAsync(collectionUri);
            Resource databaseResource = await client.ReadDatabaseAsync(databaseUri);
            var collectionOffer = GetOffer(collectionResource, client);
            var collectionOfferResource = await client.ReadOfferAsync(collectionOffer.SelfLink);
            var collectionOfferContent = collectionOfferResource.Resource.GetPropertyValue<OfferContentV2>("content");
            var databaseOffer = GetOffer(databaseResource, client);
            var dbOfferResource = await client.ReadOfferAsync(databaseOffer.SelfLink);
            var dbOfferContent = dbOfferResource.Resource.GetPropertyValue<OfferContentV2>("content");

            // Assert
            Assert.Equal(cosmosCacheFactorySettings.NewCollectionDefaultDTU, collectionOfferContent.OfferThroughput);
            Assert.Equal(dbOfferThroughput, dbOfferContent.OfferThroughput);

            // Cleanup
            await factory.DocumentClient.DeleteDocumentCollectionAsync(collectionUri);
            await factory.DocumentClient.DeleteDatabaseAsync(databaseUri);
        }
    }

    [Fact, IsIntegration]
    public async Task Create_WithDBSharedRUSetting_WithoutCollectionRUvalue_CollectionOfferSetToDefaultRUsAndNoDbOffer()
    {
        // Arrange
        var tempCollectionName = Guid.NewGuid().ToString();
        var collectionUri = UriFactory.CreateDocumentCollectionUri(LocalClusterCosmosDb.DbName, tempCollectionName);
        var databaseUri = UriFactory.CreateDatabaseUri(LocalClusterCosmosDb.DbName);
        var cosmosCacheFactorySettings = new CosmosCacheFactorySettings
        {
            UseKeyAsPartitionKey = true,
            DatabaseSharedRUs = true
        };

        using (var factory = new CosmosCacheFactory(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey, LocalClusterCosmosDb.DbName, cosmosCacheFactorySettings))
        using (var client = new DocumentClient(LocalClusterCosmosDb.ConnectionURI, LocalClusterCosmosDb.AccessKey))
        {
            //ensure temp collection is created
            factory.Create<SimpleObject>(tempCollectionName);

            Resource collectionResource = await client.ReadDocumentCollectionAsync(collectionUri);
            Resource databaseResource = await client.ReadDatabaseAsync(databaseUri);
            var collectionOffer = GetOffer(collectionResource, client);
            var collectionOfferResource = await client.ReadOfferAsync(collectionOffer.SelfLink);
            var collectionOfferContent = collectionOfferResource.Resource.GetPropertyValue<OfferContentV2>("content");
            var databaseOffer = GetOffer(databaseResource, client);

            // Assert
            Assert.Null(databaseOffer);
            Assert.Equal(400, collectionOfferContent.OfferThroughput);

            // Cleanup
            await factory.DocumentClient.DeleteDocumentCollectionAsync(collectionUri);
        }
    }

    private static Offer GetOffer(Resource resource, DocumentClient client)
    {
        var sqlQuerySpec = new SqlQuerySpec("SELECT * FROM offers o WHERE o.resource = @dbLink",
            new SqlParameterCollection(new[] {new SqlParameter {Name = "@dbLink", Value = resource.SelfLink}}));
        return client.CreateOfferQuery(sqlQuerySpec).AsEnumerable().FirstOrDefault();
    }
}
