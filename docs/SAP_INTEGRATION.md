# SAP integration target

The agreed free-access path is SAP Business Accelerator Hub sandbox for supported API contract tests, alongside a replaceable stateful SAP simulator for complete synthetic billing workflows. SAP BTP trial is not itself an S/4HANA billing system. The S/4HANA Cloud Public Edition guided trial does not offer the integration capabilities needed for this application.

Before implementing a live adapter, select the actual billing/project API and verify its sandbox availability, supported operations and response persistence in the current Hub catalogue. PBD/PBR/BDR acronyms in requirements do not by themselves identify a released API contract. Record exact API/version/operation, authentication and mapped fields. Do not assume a generic Billing Document API supplies every project billing action.

The Hub API key must remain in a local environment variable for testing or Azure Key Vault for deployment, never in GitHub/source or chat. The .NET backend adapter owns SAP credentials and calls; the browser and AI never receive the key. Runtime configuration separates Simulator and SapSandbox modes. External sandbox tests must record actual HTTP/contract evidence and distinguish returned samples from persistent business effects.

A real sandbox call has not yet been executed. No SAP credential/account is connected in this session. The simulator still requires a separate service and workflow adapter; current API mock references do not prove SAP integration.

Official references:
- https://help.sap.com/docs/SAP_API_BUSINESS_HUB/e56a6c50d31541ea826021dc8e721a53/6a86b2433feb43899a9639d080dba752.html
- https://help.sap.com/docs/business-accelerator-hub/sap-business-accelerator-hub/trying-out-apis
- https://www.sap.com/india/products/erp/s4hana/trial.html
