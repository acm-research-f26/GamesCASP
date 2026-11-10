[CmdletBinding()]
param([string]$ExampleOutput, [switch]$CompileUnity)

$ErrorActionPreference = 'Stop'
$verificationDirectory = Join-Path $PSScriptRoot 'ScaspUnityGame/Temp/ExperimentMetricsVerification'
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'A .NET SDK is required.' }
$sdkMajor = $sdkVersion.Split('.')[0]
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net$sdkMajor.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/Scripts/ExperimentMetrics.cs" />
    <Compile Include="../../Assets/Scripts/ExperimentCsv.cs" />
    <Compile Include="../../Assets/Editor/ExperimentMetricsVerification.cs" />
    <Compile Include="../../Assets/Scripts/ExperimentMeasurements.cs" />
    <Compile Include="../../Assets/Scripts/ExperimentRoleStrategies.cs" />
    <Compile Include="../../Assets/Editor/ExperimentMeasurementsVerification.cs" />
  </ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $verificationDirectory 'Verification.csproj') -Value $project
# No packages or network are needed for the pure C# verification.
Set-Content -LiteralPath (Join-Path $verificationDirectory 'NuGet.Config') -Value '<configuration><packageSources><clear /></packageSources></configuration>'
$runArguments = @('run', '--project', (Join-Path $verificationDirectory 'Verification.csproj'), '--')
if ($ExampleOutput) { $runArguments += @('--example-output', $ExampleOutput) }
& dotnet @runArguments
if ($LASTEXITCODE -ne 0) { throw 'Experiment metric verification failed.' }

if ($CompileUnity) {
    # Use Unity-generated references, never substitutes for gameplay classes.
    $unityProject = Join-Path $PSScriptRoot 'ScaspUnityGame'
    [xml]$runtimeProject = Get-Content (Join-Path $unityProject 'Assembly-CSharp.csproj')
    [xml]$editorProject = Get-Content (Join-Path $unityProject 'Assembly-CSharp-Editor.csproj')
    $references = @($runtimeProject.Project.ItemGroup.Reference.HintPath) + @($editorProject.Project.ItemGroup.Reference.HintPath)
    $seenReferences = @{}
    $compileArguments = @('/nostdlib+', '/target:library', '/langversion:9',
        ('/out:"' + (Join-Path $verificationDirectory 'UnityCompile.dll') + '"'),
        ('/define:' + ($runtimeProject.Project.PropertyGroup.DefineConstants | Where-Object { $_ })))
    foreach ($reference in $references) {
        if (-not $reference) { continue }
        $referenceName = [IO.Path]::GetFileName($reference)
        if ($referenceName -eq 'Assembly-CSharp.dll' -or $seenReferences.ContainsKey($referenceName)) { continue }
        $seenReferences[$referenceName] = $true
        $referencePath = if ([IO.Path]::IsPathRooted($reference)) { $reference } else { Join-Path $unityProject $reference }
        $compileArguments += '/reference:"' + $referencePath + '"'
    }
    $compileArguments += Get-ChildItem (Join-Path $unityProject 'Assets') -Recurse -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' }
    $responsePath = Join-Path $verificationDirectory 'compile.rsp'
    Set-Content -LiteralPath $responsePath -Value $compileArguments
    $sdkRootLine = & dotnet --list-sdks | Where-Object { $_.StartsWith($sdkVersion + ' ') } | Select-Object -First 1
    if ($sdkRootLine -notmatch '\[(.+)\]') { throw 'Cannot locate the active .NET SDK compiler.' }
    $compiler = Join-Path $Matches[1] "$sdkVersion/Roslyn/bincore/csc.dll"
    & dotnet $compiler "@$responsePath"
    if ($LASTEXITCODE -ne 0) { throw 'Unity source compilation failed.' }
}
