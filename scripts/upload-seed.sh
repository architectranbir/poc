#!/usr/bin/env bash
set -euo pipefail
: "${AZ_RG:=rg-ledgerpilot-sandbox}"
: "${AZ_LOCATION:=uksouth}"
: "${AZ_STORAGE:?Set AZ_STORAGE to a unique lowercase storage account name}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
az group create -g "$AZ_RG" -l "$AZ_LOCATION"
az storage account create -g "$AZ_RG" -n "$AZ_STORAGE" -l "$AZ_LOCATION" --sku Standard_LRS --kind StorageV2 --allow-blob-public-access false
az storage container create --account-name "$AZ_STORAGE" --name synthetic --auth-mode login
az storage blob upload-batch --account-name "$AZ_STORAGE" --destination synthetic --source "$ROOT/seed" --auth-mode login --overwrite true
echo "BLOB_CONTAINER=https://${AZ_STORAGE}.blob.core.windows.net/synthetic"
