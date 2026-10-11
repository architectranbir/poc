#!/usr/bin/env python3
"""Replace the existing synthetic demo image through ARM; retain its full template."""
import copy
import json
import os
import subprocess
import time
import urllib.request

API_VERSION = '2024-03-01'
subscription = os.environ['AZURE_SUBSCRIPTION_ID']
group = os.environ['RESOURCE_GROUP']
app = os.environ['APP_NAME']
image = os.environ['RELEASE_IMAGE']
base = f'https://management.azure.com/subscriptions/{subscription}/resourceGroups/{group}/providers/Microsoft.App/containerApps/{app}'


def arm(path='', method='GET', body=None):
    args = ['az', 'rest', '--method', method, '--url', base + path + '?api-version=' + API_VERSION]
    if body is not None:
        args += ['--body', json.dumps(body)]
    response = subprocess.run(args, check=True, text=True, capture_output=True)
    return json.loads(response.stdout) if response.stdout.strip() else {}


def demo_image(resource):
    containers = resource['properties']['template']['containers']
    candidates = [container for container in containers if container['name'] == 'demo']
    if len(candidates) != 1:
        raise ValueError('Expected exactly one demo container in the existing resource.')
    return candidates[0]['image']


def deactivate_active():
    page = arm('/revisions')
    for revision in page.get('value', []):
        if revision['properties'].get('active'):
            arm('/revisions/' + revision['name'] + '/deactivate', 'POST')


def replace(desired_image):
    existing = arm()
    template = copy.deepcopy(existing['properties']['template'])
    candidates = [container for container in template['containers'] if container['name'] == 'demo']
    if len(candidates) != 1:
        raise ValueError('Expected exactly one demo container.')
    candidates[0]['image'] = desired_image
    template.pop('revisionSuffix', None)
    # Patch only the complete existing template. Configuration, identity, ingress,
    # registries and secrets are neither read back as values nor overwritten.
    arm(method='PATCH', body={'location': existing['location'], 'properties': {'template': template}})
    for _ in range(120):
        resource = arm()
        props = resource['properties']
        if props.get('provisioningState') == 'Failed':
            raise RuntimeError('Azure reported failed demo revision provisioning.')
        if props.get('provisioningState') == 'Succeeded' and demo_image(resource) == desired_image:
            revision = props.get('latestRevisionName')
            if revision:
                arm('/revisions/' + revision + '/activate', 'POST')
                return resource
        time.sleep(5)
    raise TimeoutError('Azure demo revision provisioning did not complete.')


def healthy(url):
    for attempt in range(36):
        try:
            with urllib.request.urlopen(url + '/health', timeout=10) as response:
                if response.status == 200:
                    return
        except OSError:
            pass
        if attempt < 35:
            time.sleep(5)
    raise TimeoutError('Deployed demo health check did not succeed.')


def main():
    original = arm()
    previous_image = demo_image(original)
    if not original['properties'].get('latestRevisionName'):
        raise ValueError('Existing demo revision is required; this script does not create resources.')
    url = 'https://' + original['properties']['configuration']['ingress']['fqdn']
    expected = subprocess.check_output(['docker', 'run', '--rm', '--entrypoint', 'cat', image, '/app/wwwroot/index.html'])
    try:
        if previous_image != image:
            deactivate_active()
            replace(image)
        healthy(url)
        with urllib.request.urlopen(url + '/', timeout=15) as response:
            if response.read() != expected:
                raise RuntimeError('Served frontend HTML differs from the exact release image.')
    except Exception:
        print('Release verification failed; restoring the previous demo image.', flush=True)
        deactivate_active()
        replace(previous_image)
        healthy(url)
        raise
    print('Verified updated frontend at ' + url)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a') as output:
            output.write(f'Verified frontend image `{image}` on {url}\n')


if __name__ == '__main__':
    main()
