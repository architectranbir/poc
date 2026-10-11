import { chromium } from "playwright";
import { readFile, mkdir } from "node:fs/promises";
import assert from "node:assert/strict";
const web = process.env.WEB_URL || "http://localhost:5173";
const projects = JSON.parse(
  await readFile(new URL("../../seed/projects.json", import.meta.url)),
);
const lines = JSON.parse(
  await readFile(new URL("../../seed/wip-lines.json", import.meta.url)),
);
const snapshots = Object.fromEntries(
  projects.map((project) => {
    const projectLines = lines.filter((line) => line.projectId === project.id);
    return [
      project.id,
      {
        project,
        lines: projectLines,
        total: projectLines.reduce((sum, line) => sum + line.amount, 0),
        financialHash: "ui-test-fixture",
        financeRequired: false,
        findings: [
          {
            rule: "W-03",
            severity: "CONFIRM",
            id: "fixture-" + project.id,
            dependencyHash: "fixture-" + project.id,
            message: "Review the selected project entries before billing.",
          },
        ],
        position: {
          lines: projectLines.map((line) => ({
            lineId: line.id,
            bill: line.amount,
          })),
        },
      },
    ];
  }),
);
await mkdir("artifacts/browser", { recursive: true });
const browser = await chromium.launch({
  headless: true,
  ...(process.env.CHROMIUM_EXECUTABLE
    ? { executablePath: process.env.CHROMIUM_EXECUTABLE }
    : {}),
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1050 },
});
const page = await context.newPage();
const errors = [];
page.on("pageerror", (error) => errors.push(error.message));
let failNext = false;
let unblock;
let delayNext = false;
const requests = [];
await context.route("**/api/**", async (route) => {
  const request = route.request();
  const path = new URL(request.url()).pathname;
  let result;
  if (path === "/api/projects") result = projects;
  else if (path.endsWith("/wip")) result = snapshots[path.split("/")[3]];
  else if (path === "/api/ai/explain") {
    const body = request.postDataJSON();
    requests.push(body);
    assert.deepEqual(Object.keys(body).sort(), ["projectId", "userMessage"]);
    if (delayNext) {
      delayNext = false;
      await new Promise((resolve) => {
        unblock = resolve;
      });
    }
    if (failNext) {
      failNext = false;
      return route.fulfill({
        status: 503,
        json: { error: "Fixture: assistant temporarily unavailable" },
      });
    }
    result = {
      text: `Test response for ${body.projectId}.\nThis is a mocked UI test, not live Foundry inference.`,
    };
  } else throw new Error("Unexpected UI request: " + path);
  await route.fulfill({
    json: result,
    headers: { "Access-Control-Allow-Origin": "*" },
  });
});
try {
  await page.goto(web);
  await page
    .getByRole("heading", { name: "Billing workspace", exact: true })
    .waitFor();
  const composer = page.getByLabel("Message billing assistant", {
    exact: true,
  });
  await page
    .getByRole("button", { name: "Explain the current billing findings." })
    .waitFor({ state: "visible" });
  await page.waitForFunction(
    () => !document.querySelector("#chat-input").disabled,
  );
  await page.screenshot({
    path: "artifacts/browser/conversation-desktop.png",
    fullPage: false,
  });
  delayNext = true;
  await composer.fill("First question");
  await composer.press("Enter");
  await page
    .getByRole("status")
    .filter({ hasText: "Checking your project" })
    .waitFor();
  assert.equal(await composer.isDisabled(), true);
  assert.equal(
    await page.getByLabel("Current project", { exact: true }).isDisabled(),
    true,
  );
  unblock();
  await page
    .getByText(`Test response for ${projects[0].id}.`, { exact: false })
    .waitFor();
  assert.equal(requests.at(-1).userMessage, "First question");
  await composer.fill("Second question");
  await composer.press("Shift+Enter");
  assert.equal(await composer.inputValue(), "Second question\n");
  await page.getByRole("button", { name: "Send message", exact: true }).click();
  await page.waitForFunction(
    () =>
      document.querySelectorAll(".message-assistant").length === 2 &&
      !document.querySelector(".typing"),
  );
  assert.equal(await page.locator(".message-user").count(), 2);
  await page
    .getByLabel("Current project", { exact: true })
    .selectOption(projects[1].id);
  await page.waitForFunction(
    () => !document.querySelector("#chat-input").disabled,
  );
  assert.equal(await page.locator(".message").count(), 0);
  await page
    .getByRole("button", { name: "Explain the current billing findings." })
    .click();
  await page
    .getByText(`Test response for ${projects[1].id}.`, { exact: false })
    .waitFor();
  await page
    .getByLabel("Current project", { exact: true })
    .selectOption(projects[0].id);
  await page.waitForFunction(
    () => !document.querySelector("#chat-input").disabled,
  );
  assert.equal(await page.locator(".message-user").count(), 2);
  failNext = true;
  await composer.fill("Retry question");
  await page.getByRole("button", { name: "Send message", exact: true }).click();
  await page.locator(".message-error").waitFor();
  await page.getByRole("button", { name: "Try again", exact: true }).click();
  await page.waitForFunction(
    () =>
      document.querySelectorAll(".message-assistant").length === 4 &&
      !document.querySelector(".typing"),
  );
  assert.equal(requests.at(-1).userMessage, "Retry question");
  await page
    .getByRole("button", { name: "New conversation", exact: true })
    .click();
  assert.equal(await page.locator(".message").count(), 0);
  await page
    .getByLabel("Current project", { exact: true })
    .selectOption(projects[1].id);
  await page.waitForFunction(
    () => !document.querySelector("#chat-input").disabled,
  );
  assert.equal(await page.locator(".message-user").count(), 1);
  await page
    .getByRole("button", { name: "New conversation", exact: true })
    .click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => window.scrollTo(0, 0));
  assert.ok(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
    "Mobile layout overflows",
  );
  await page.screenshot({
    path: "artifacts/browser/conversation-mobile.png",
    fullPage: true,
  });
  assert.deepEqual(errors, []);
  console.log(
    "PASS: conversation prompts, pending state, keyboard send, multiline input, per-project history, error/retry, clearing and mobile layout; mocked API only",
  );
} finally {
  await browser.close();
}
