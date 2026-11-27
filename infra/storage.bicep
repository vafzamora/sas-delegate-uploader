@description('Azure region for the Storage Account.')
param location string

@minLength(3)
@maxLength(24)
param storageAccountName string

param apiPrincipalId string

@allowed([
  'User'
  'ServicePrincipal'
])
param apiPrincipalType string

var storageBlobDelegatorRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'
)

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
      ipRules: []
      virtualNetworkRules: []
    }
  }
  tags: {
    workload: 'sas-upload-poc'
    environment: 'development'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource uploadsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'uploads'
  properties: {
    publicAccess: 'None'
  }
}

resource delegatorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, apiPrincipalId, storageBlobDelegatorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: storageBlobDelegatorRoleId
    principalId: apiPrincipalId
    principalType: apiPrincipalType
  }
}

output storageAccountName string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output storageAccountId string = storageAccount.id
output containerName string = uploadsContainer.name
