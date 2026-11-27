targetScope = 'subscription'

@description('Azure region for all resources.')
param location string = 'brazilsouth'

@description('Resource group dedicated to the SAS Upload PoC.')
param resourceGroupName string = 'sasupload-poc-rg'

@minLength(3)
@maxLength(24)
@description('Globally unique Storage Account name.')
param storageAccountName string

@description('Object ID of the identity that runs the API and generates user delegation SAS.')
param apiPrincipalId string

@allowed([
  'User'
  'ServicePrincipal'
])
param apiPrincipalType string = 'User'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: resourceGroupName
  location: location
  tags: {
    workload: 'sas-upload-poc'
    environment: 'development'
  }
}

module storage 'storage.bicep' = {
  name: 'storage-deployment'
  scope: resourceGroup
  params: {
    location: location
    storageAccountName: storageAccountName
    apiPrincipalId: apiPrincipalId
    apiPrincipalType: apiPrincipalType
  }
}

output resourceGroupName string = resourceGroup.name
output storageAccountName string = storage.outputs.storageAccountName
output blobEndpoint string = storage.outputs.blobEndpoint
output storageAccountId string = storage.outputs.storageAccountId
output containerName string = storage.outputs.containerName
