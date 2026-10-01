import jetbrains.buildServer.configs.kotlin.*
import jetbrains.buildServer.configs.kotlin.buildSteps.script
import jetbrains.buildServer.configs.kotlin.triggers.vcs
import jetbrains.buildServer.configs.kotlin.triggers.finishBuildTrigger

version = "2026.2"

project {
    description = "CX1 back-office API build and IIS deployment"
    buildType(ApiCi)
    buildType(ApiDeploy)
}

object ApiCi : BuildType({
    name = "api-ci"
    vcs {
        root(DslContext.settingsRoot)
        checkoutMode = CheckoutMode.ON_AGENT
        cleanCheckout = true
        branchFilter = "+:<default>"
    }
    triggers { vcs { branchFilter = "+:<default>" } }
    artifactRules = "artifacts/teamcity-api/api.zip => api\nartifacts/teamcity-api/api-manifest.json => api\nartifacts/teamcity-api/migrations.sql => database"
    requirements { contains("teamcity.agent.jvm.os.name", "Windows") }
    steps { script { name = "Package API and generate migrations"; scriptContent = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/ci/build-api.ps1" } }
})

object ApiDeploy : BuildType({
    name = "api-deploy"
    type = BuildTypeSettings.Type.DEPLOYMENT
    maxRunningBuilds = 1
    vcs {
        root(DslContext.settingsRoot)
        checkoutMode = CheckoutMode.ON_AGENT
        cleanCheckout = true
        branchFilter = "+:<default>"
    }
    params {
        param("env.CX1_INSTALL_ROOT", "D:\\Websites\\cx1-admin-api-dev.gyongyos.co.uk")
        param("env.CX1_APP_DIRECTORY", "www")
        param("env.CX1_APP_POOL", "Cx1AdminDev")
        param("env.CX1_HEALTH_URL", "https://cx1-admin-api-dev.gyongyos.co.uk/health/live")
        password("env.CX1_ORIGIN_SECRET", "credentialsJSON:d1dd87fc-1f36-4678-a7da-89c69d627bfb")
    }
    requirements { contains("teamcity.agent.jvm.os.name", "Windows"); equals("env.ZENX_ROLE", "deploy") }
    steps { script { name = "Deploy API and verify origin health"; scriptContent = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/ci/deploy-api.ps1" } }
    dependencies {
        dependency(ApiCi) {
            snapshot { onDependencyFailure = FailureAction.FAIL_TO_START; onDependencyCancel = FailureAction.CANCEL }
            artifacts { buildRule = sameChainOrLastFinished(); artifactRules = "api/api.zip => incoming\napi/api-manifest.json => incoming"; cleanDestination = true }
        }
    }
    triggers {
        finishBuildTrigger {
            buildType = "${ApiCi.id}"
            successfulOnly = true
            branchFilter = "+:<default>"
        }
    }
})
