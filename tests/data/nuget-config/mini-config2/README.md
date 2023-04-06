This should fail because the registration catlog URL is a 404 for this file
https://azuresdkartifacts.blob.core.windows.net/azure-sdk-tools/virtualcatalog/data/7d210ec8-7ada-41d5-8c70-376670b110f3.json


Scan Result: success: JSON file created at: foo.json

    Errors or Warnings at the package level
       Sample@ with purl: 

        Errors or Warnings at the dependencies level
            Microsoft.Internal.NetSdkBuild.Mgmt.Tools@0.9.0 with purl: 
            WARNING: Failed to get remote metadata for name: 'Microsoft.Internal.NetSdkBuild.Mgmt.Tools' version: '0.9.0'. System.Net.WebException: The remote server returned an error: (404) The specified blob does not exist..
   at System.Net.HttpWebRequest.GetResponse()
   at NugetInspector.NugetApi.GetPackageDownload(PackageIdentity identity, Boolean with_details) in ./nuget-inspector/NugetApi.cs:line 515
   at NugetInspector.BasePackage.UpdateWithRemoteMetadata(NugetApi nugetApi, Boolean with_details) in ./nuget-inspector/Models.cs:line 340
   at NugetInspector.BasePackage.Update(NugetApi nugetApi, Boolean with_details) in ./nuget-inspector/Models.cs:line 312
