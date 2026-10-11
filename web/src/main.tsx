import {
  hostedDemo,
  localDemo,
  configurationError,
  initializeAuth,
  signedInName,
  signIn,
  signOut,
  accessToken,
} from "./auth";
import React, { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import "./style.css";
const API =
  import.meta.env.VITE_API_URL || (hostedDemo ? "" : "http://localhost:8080");
type Project = {
  id: string;
  name: string;
  unit: string;
  requestor: string;
  financeApprover: string;
};
type Finding = {
  rule: string;
  severity: string;
  message: string;
  id: string;
  dependencyHash: string;
};
type Message = {
  id: string;
  role: "user" | "assistant";
  text: string;
  state: "pending" | "ready" | "error";
  at: string;
};
function Icon({ name, size = 20 }: { name: string; size?: number }) {
  const paths: Record<string, React.ReactNode> = {
    chat: (
      <>
        <path d="M21 11.5a8.4 8.4 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.4 8.4 0 0 1-3.8-.9L3 21l1.9-5.7a8.4 8.4 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.4 8.4 0 0 1 3.8-.9h.5a8.5 8.5 0 0 1 8 8z" />
      </>
    ),
    grid: (
      <>
        <rect x="3" y="3" width="7" height="7" rx="1" />
        <rect x="14" y="3" width="7" height="7" rx="1" />
        <rect x="3" y="14" width="7" height="7" rx="1" />
        <rect x="14" y="14" width="7" height="7" rx="1" />
      </>
    ),
    check: (
      <>
        <path d="m9 12 2 2 4-4" />
        <path d="M12 3 4 6v6c0 5 8 9 8 9s8-4 8-9V6z" />
      </>
    ),
    file: (
      <>
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <path d="M14 2v6h6M8 13h8M8 17h5" />
      </>
    ),
    arrow: (
      <>
        <path d="M12 19V5m-6 6 6-6 6 6" />
      </>
    ),
    plus: <path d="M12 5v14M5 12h14" />,
    spark: (
      <>
        <path d="m12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5z" />
      </>
    ),
  };
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {paths[name] || paths.chat}
    </svg>
  );
}
function App() {
  const [selectedLines, setSelectedLines] = useState<string[]>([]);
  const [batchId, setBatchId] = useState(() => crypto.randomUUID());
  const [batchReason, setBatchReason] = useState("");
  const [batchAction, setBatchAction] = useState("POSTPONE");
  const [adjustmentLine, setAdjustmentLine] = useState("");
  const [adjustmentAction, setAdjustmentAction] = useState("POSTPONE");
  const [adjustmentAmount, setAdjustmentAmount] = useState("");
  const [adjustmentReason, setAdjustmentReason] = useState("");
  const [adjustmentEvent, setAdjustmentEvent] = useState(() =>
    crypto.randomUUID(),
  );
  const [history, setHistory] = useState<any[]>([]);
  const [vatConfirmed, setVatConfirmed] = useState(false);
  const [currencyConfirmed, setCurrencyConfirmed] = useState(false);
  const [commercial, setCommercial] = useState<any>(null);
  const [writeUpEvent, setWriteUpEvent] = useState(() => crypto.randomUUID());
  const [writeUpAmount, setWriteUpAmount] = useState("");
  const [writeUpReason, setWriteUpReason] = useState("");
  const [authReady, setAuthReady] = useState(false);
  const [user, setUser] = useState("");
  const [acknowledged, setAcknowledged] = useState<string[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [id, setId] = useState("");
  const [data, setData] = useState<any>(null);
  const [proposed, setProposed] = useState(100000);
  const [review, setReview] = useState<any>(null);
  const [chat, setChat] = useState("");
  const [answer, setAnswer] = useState("");
  const [pbd, setPbd] = useState<any>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const request = async (path: string, opts?: RequestInit) => {
    setError("");
    const token = await accessToken();
    const res = await fetch(API + path, {
      ...opts,
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...opts?.headers,
      },
    });
    if (!res.ok) {
      const t = await res.text();
      throw new Error(t);
    }
    return res.json();
  };
  useEffect(() => {
    initializeAuth()
      .then(() => {
        setUser(signedInName());
        setAuthReady(true);
      })
      .catch((e) => setError(e.message));
  }, []);
  useEffect(() => {
    if (!authReady || configurationError || (!localDemo && !user)) return;
    request("/api/projects")
      .then((p: Project[]) => {
        setProjects(p);
        if (p.length) setId(p[0].id);
      })
      .catch((e) => setError(e.message));
  }, [authReady, user]);
  useEffect(() => {
    let active = true;
    setData(null);
    if (id)
      request(`/api/projects/${id}/wip`)
        .then((value) => {
          if (active) setData(value);
        })
        .catch((e) => {
          if (active) setError(e.message);
        });
    return () => {
      active = false;
    };
  }, [id]);
  const go = async (action: () => Promise<void>) => {
    setBusy(true);
    try {
      await action();
    } catch (e: any) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  };

  const [threads, setThreads] = useState<Record<string, Message[]>>({});
  const thread = threads[id] || [];
  const chatEnd = useRef<HTMLDivElement>(null);
  useEffect(() => {
    chatEnd.current?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [thread.length, thread.at(-1)?.state]);
  const projectReady = Boolean(data && data.project?.id === id);
  const sendMessage = async (prompt = chat) => {
    const text = prompt.trim();
    if (!text || text.length > 4000 || busy || !projectReady) return;
    const projectId = id;
    const responseId = crypto.randomUUID();
    const at = new Date().toLocaleTimeString("en-GB", {
      hour: "2-digit",
      minute: "2-digit",
    });
    setThreads((old) => ({
      ...old,
      [projectId]: [
        ...(old[projectId] || []),
        { id: crypto.randomUUID(), role: "user", text, state: "ready", at },
        { id: responseId, role: "assistant", text: "", state: "pending", at },
      ],
    }));
    setChat("");
    setBusy(true);
    const finish = (text: string, state: Message["state"]) =>
      setThreads((old) => ({
        ...old,
        [projectId]: (old[projectId] || []).map((message) =>
          message.id === responseId ? { ...message, text, state } : message,
        ),
      }));
    try {
      const response = await request("/api/ai/explain", {
        method: "POST",
        body: JSON.stringify({ userMessage: text, projectId }),
      });
      finish(
        response.text || "No response was returned. Please try again.",
        "ready",
      );
    } catch (error: unknown) {
      finish(
        error instanceof Error
          ? error.message
          : "The assistant could not respond. Please try again.",
        "error",
      );
    } finally {
      setBusy(false);
    }
  };
  const money = (value: unknown) =>
    new Intl.NumberFormat("en-GB", { maximumFractionDigits: 2 }).format(
      Number(value || 0),
    );
  const blocking = (data?.findings || []).filter(
    (finding: Finding) => finding.severity === "BLOCK",
  ).length;
  const prompts = [
    "Explain the current billing findings.",
    "What should I review before creating a bill?",
    "Explain the available WIP and adjustments.",
  ];

  if (configurationError || !authReady || (!localDemo && !user))
    return (
      <main className="auth-screen">
        <div className="auth-card">
          <div className="brand-mark">L</div>
          <h1>LedgerPilot</h1>
          <p>
            {configurationError ||
              (!authReady
                ? "Preparing your workspace…"
                : "Sign in to your billing workspace.")}
          </p>
          {authReady && !configurationError && (
            <button
              disabled={busy}
              onClick={() =>
                go(async () => {
                  await signIn();
                  setUser(signedInName());
                })
              }
            >
              Sign in with Microsoft
            </button>
          )}
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
        </div>
      </main>
    );
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="#conversation">
          <span className="brand-mark">L</span>
          <span>
            LedgerPilot<small>BILLING INTELLIGENCE</small>
          </span>
        </a>
        <span className="nav-caption">WORKSPACE</span>
        <nav aria-label="Workspace navigation">
          <a className="nav-active" href="#conversation">
            <Icon name="chat" />
            Conversation
          </a>
          <a href="#wip">
            <Icon name="grid" />
            WIP & adjustments
          </a>
          <a href="#review">
            <Icon name="check" />
            Billing review
          </a>
          <a href="#document">
            <Icon name="file" />
            SAP document
          </a>
        </nav>
        <div className="sidebar-note">
          <span className="status-dot" />
          Synthetic sandbox
          <p>Explore the billing journey with test data and a SAP simulator.</p>
        </div>
        <div className="profile">
          <span className="avatar">{(user || "Demo").slice(0, 1)}</span>
          <div>
            {localDemo ? "Demo workspace" : user}
            <small>{localDemo ? "Synthetic identity" : "Signed in"}</small>
          </div>
        </div>
        {!localDemo && (
          <button
            className="signout"
            disabled={busy}
            onClick={() =>
              go(async () => {
                await signOut();
                setUser("");
                setProjects([]);
                setId("");
                setData(null);
                setPbd(null);
                setReview(null);
                setAnswer("");
                setAcknowledged([]);
                setThreads({});
              })
            }
          >
            Sign out
          </button>
        )}
      </aside>
      <main className="workspace">
        <header className="topbar">
          <div className="breadcrumbs">
            Workspace <span>/</span> Billing assistant
          </div>
          <span className="environment">
            <span className="status-dot" />
            Synthetic demo
          </span>
        </header>
        <section className="hero">
          <div>
            <span className="eyebrow">A CLEARER WAY TO BILL</span>
            <h1>Billing workspace</h1>
            <p>
              Review your WIP. Understand the findings. Prepare your next bill.
            </p>
          </div>
          <button
            className="secondary"
            disabled={busy || !id}
            onClick={() => {
              setThreads((old) => ({ ...old, [id]: [] }));
              setChat("");
            }}
          >
            <Icon name="plus" size={17} />
            New conversation
          </button>
        </section>
        <section className="project-toolbar">
          <label htmlFor="project-picker">Current project</label>
          <select
            id="project-picker"
            disabled={busy}
            value={id}
            onChange={(e) => {
              setId(e.target.value);
              setChat("");
              setAnswer("");
              setSelectedLines([]);
              setBatchReason("");
              setBatchId(crypto.randomUUID());
              setAdjustmentLine("");
              setAdjustmentAmount("");
              setAdjustmentReason("");
              setHistory([]);
              setAdjustmentEvent(crypto.randomUUID());
              setVatConfirmed(false);
              setCurrencyConfirmed(false);
              setCommercial(null);
              setWriteUpAmount("");
              setWriteUpReason("");
              setWriteUpEvent(crypto.randomUUID());
              setAcknowledged([]);
              setReview(null);
              setPbd(null);
            }}
          >
            {projects.map((p) => (
              <option key={p.id} value={p.id}>
                {p.id} · {p.name}
              </option>
            ))}
          </select>
          <p className="scope-caption">
            Unit: {data?.project?.unit} · Authorised demo actor:{" "}
            {data?.project?.requestor}
          </p>
        </section>
        <div className="metrics">
          <div>
            <span>Available WIP</span>
            <strong>
              {projectReady ? money(data.total) : "—"}{" "}
              <small>{data?.project?.currency || ""}</small>
            </strong>
            <p>Current source position</p>
          </div>
          <div>
            <span>WIP entries</span>
            <strong>{projectReady ? data.lines.length : "—"}</strong>
            <p>Within your selected project</p>
          </div>
          <div>
            <span>Review findings</span>
            <strong>
              {projectReady ? data.findings.length : "—"}{" "}
              <small className={blocking ? "flag warning" : "flag"}>
                {projectReady
                  ? blocking
                    ? `${blocking} blocking`
                    : "No blockers"
                  : "Loading"}
              </small>
            </strong>
            <p>Review before creating a document</p>
          </div>
          <div>
            <span>Document status</span>
            <strong className="status-value">
              {pbd?.status?.replaceAll("_", " ") || "Not created"}
            </strong>
            <p>
              {pbd ? `Version ${pbd.version}` : "Start with a billing review"}
            </p>
          </div>
        </div>
        {error && (
          <div className="error" role="alert">
            {error}
          </div>
        )}
        {answer && (
          <div className="action-feedback" role="status">
            {answer}
          </div>
        )}
        <div className="primary-grid">
          <section className="panel conversation" id="conversation">
            <div className="panel-heading">
              <div className="heading-icon">
                <Icon name="spark" />
              </div>
              <div>
                <h2>Billing assistant</h2>
                <p>Ask about your selected project</p>
              </div>
              <span className="subtle-badge">Read-only guidance</span>
            </div>
            <div
              className="conversation-history"
              role="log"
              aria-label="Conversation history"
              aria-live="polite"
              aria-relevant="additions text"
            >
              {!thread.length && (
                <div className="chat-welcome">
                  <div className="welcome-orbit">
                    <Icon name="spark" size={32} />
                  </div>
                  <span className="eyebrow">LET’S MAKE IT CLEAR</span>
                  <h3>
                    What would you like to
                    <br />
                    understand about this bill?
                  </h3>
                  <p>
                    Start with your findings or ask what to review next.
                    <br />
                    Replies use current server-side project data.
                  </p>
                  <div className="prompt-list">
                    {prompts.map((prompt, index) => (
                      <button
                        key={prompt}
                        className="prompt"
                        disabled={busy || !projectReady}
                        onClick={() => void sendMessage(prompt)}
                      >
                        <Icon
                          name={["check", "file", "grid"][index]}
                          size={17}
                        />
                        <span>{prompt}</span>
                        <span aria-hidden="true">↗</span>
                      </button>
                    ))}
                  </div>
                </div>
              )}
              {thread.map((message) => (
                <article
                  key={message.id}
                  className={`message message-${message.role} ${message.state === "error" ? "message-error" : ""}`}
                >
                  <div className="message-avatar">
                    {message.role === "assistant" ? (
                      <Icon name="spark" size={16} />
                    ) : (
                      "You"
                    )}
                  </div>
                  <div className="message-content">
                    <div className="message-meta">
                      <b>
                        {message.role === "assistant" ? "LedgerPilot" : "You"}
                      </b>
                      <time>{message.at}</time>
                    </div>
                    <div className="message-text">
                      {message.state === "pending" ? (
                        <span className="typing" role="status">
                          Checking your project…
                        </span>
                      ) : (
                        message.text
                      )}
                    </div>
                    {message.state === "error" && (
                      <button
                        className="retry"
                        disabled={busy}
                        onClick={() => {
                          const index = thread.findIndex(
                            (item) => item.id === message.id,
                          );
                          const prior = thread[index - 1];
                          if (prior?.role === "user")
                            void sendMessage(prior.text);
                        }}
                      >
                        Try again
                      </button>
                    )}
                  </div>
                </article>
              ))}
              <div ref={chatEnd} />
            </div>
            <form
              className="composer"
              onSubmit={(event) => {
                event.preventDefault();
                void sendMessage();
              }}
            >
              <label className="sr-only" htmlFor="chat-input">
                Message billing assistant
              </label>
              <textarea
                id="chat-input"
                placeholder="Ask about WIP, findings or your next step…"
                value={chat}
                maxLength={4000}
                rows={2}
                disabled={busy || !projectReady}
                onChange={(event) => setChat(event.target.value)}
                onKeyDown={(event) => {
                  if (
                    event.key === "Enter" &&
                    !event.shiftKey &&
                    !event.nativeEvent.isComposing
                  ) {
                    event.preventDefault();
                    void sendMessage();
                  }
                }}
              />
              <div className="composer-bottom">
                <span>Enter to send · Shift + Enter for a new line</span>
                <button
                  type="submit"
                  className="send"
                  aria-label="Send message"
                  disabled={busy || !projectReady || !chat.trim()}
                >
                  <Icon name="arrow" size={19} />
                </button>
              </div>
            </form>
            <p className="chat-caption">
              Review AI guidance. Billing changes require your explicit
              confirmation.
            </p>
          </section>
          <section className="panel review-panel" id="review">
            <div className="panel-heading">
              <div className="heading-icon neutral">
                <Icon name="check" />
              </div>
              <div>
                <h2>Billing review</h2>
                <p>Your controls, at a glance</p>
              </div>
            </div>
            <fieldset disabled={busy || !projectReady}>
              <div className="review-banner">
                <span className={blocking ? "flag warning" : "flag"}>
                  {!projectReady
                    ? "Loading project"
                    : blocking
                      ? "Review required"
                      : "Ready to review"}
                </span>
                <p>
                  {!projectReady
                    ? "Retrieving your billing position…"
                    : blocking
                      ? "Resolve blocking findings before preparing a document."
                      : "Review the findings and confirm the applicable checks."}
                </p>
              </div>
              <div className="findings-list">
                {(data?.findings || []).map((f: Finding, i: number) => (
                  <div className="finding" key={i}>
                    <b>{f.severity}</b> <small>{f.rule}</small>
                    <div>{f.message}</div>
                    {f.severity === "CONFIRM" && (
                      <label>
                        <input
                          type="checkbox"
                          checked={acknowledged.includes(f.dependencyHash)}
                          onChange={(e) =>
                            setAcknowledged((old) =>
                              e.target.checked
                                ? [...old, f.dependencyHash]
                                : old.filter((x) => x !== f.dependencyHash),
                            )
                          }
                        />{" "}
                        I have reviewed this finding
                      </label>
                    )}
                  </div>
                ))}
              </div>
              <div className="proposed-section">
                <h3>Proposed bill</h3>
                <label>Proposed amount</label>
                <input
                  type="number"
                  value={proposed}
                  onChange={(e) => setProposed(Number(e.target.value))}
                />
                <button
                  disabled={busy}
                  onClick={() =>
                    go(async () =>
                      setReview(
                        await request(`/api/projects/${id}/review`, {
                          method: "POST",
                          body: JSON.stringify({ proposedAmount: proposed }),
                        }),
                      ),
                    )
                  }
                >
                  Validate proposed bill
                </button>
                {review && (
                  <div className="notice">
                    Difference from WIP: £
                    {review.difference?.toLocaleString("en-GB")}
                    <p>
                      Accrual question:{" "}
                      <b>{review.accrualQuestion ? "YES" : "No"}</b>
                    </p>
                    <small>Threshold definition needs business sign-off.</small>
                  </div>
                )}
              </div>
              <div className="finance-section">
                <h2>Finance review</h2>
                <p>
                  {data?.financeRequired
                    ? "Finance approval required for this billing state."
                    : "Current adjustments are within the Finance threshold."}
                </p>
                {data?.financeRequired && (
                  <button
                    disabled={busy}
                    onClick={() =>
                      go(async () => {
                        await request(`/api/projects/${id}/finance-approval`, {
                          method: "POST",
                          body: JSON.stringify({
                            actor: data?.project?.financeApprover,
                            confirm: true,
                            stateHash: data?.financialHash,
                          }),
                        });
                        setAnswer(
                          "Synthetic Finance approval recorded for the current state.",
                        );
                      })
                    }
                  >
                    Confirm as demo Finance approver
                  </button>
                )}
              </div>
            </fieldset>
          </section>
        </div>
        <section className="panel wip-panel" id="wip">
          <div className="section-title">
            <div>
              <span className="eyebrow">YOUR BILLING POSITION</span>
              <h2>WIP & adjustments</h2>
              <p>Review entries and confirm changes to the selected project.</p>
            </div>
            <span className="subtle-badge">
              {selectedLines.length} selected
            </span>
          </div>
          <fieldset disabled={busy || !projectReady}>
            <h3>WIP lines</h3>
            <table>
              <thead>
                <tr>
                  <th>Select</th>
                  <th>Line</th>
                  <th>Person</th>
                  <th>Type</th>
                  <th>Remaining</th>
                </tr>
              </thead>
              <tbody>
                {(data?.lines || []).map((line: any) => (
                  <tr key={line.id}>
                    <td>
                      <input
                        type="checkbox"
                        aria-label={`Select ${line.id}`}
                        checked={selectedLines.includes(line.id)}
                        disabled={
                          busy ||
                          !data?.position?.lines?.find(
                            (a: any) => a.lineId === line.id,
                          )?.bill
                        }
                        onChange={(e) => {
                          setSelectedLines((old) =>
                            e.target.checked
                              ? [...old, line.id]
                              : old.filter((x) => x !== line.id),
                          );
                          setBatchId(crypto.randomUUID());
                        }}
                      />
                    </td>
                    <td>{line.id}</td>
                    <td>{line.person}</td>
                    <td>{line.type}</td>
                    <td>
                      {
                        data?.position?.lines?.find(
                          (a: any) => a.lineId === line.id,
                        )?.bill
                      }
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <h3>Adjust selected entries</h3>
            <p>
              {selectedLines.length} entries selected · Full remaining amount:{" "}
              {data?.position?.lines
                ?.filter((a: any) => selectedLines.includes(a.lineId))
                .reduce((sum: number, a: any) => sum + Number(a.bill), 0) ||
                0}{" "}
              {data?.project?.currency}
            </p>
            <label>
              Batch action
              <select
                aria-label="Batch action"
                value={batchAction}
                onChange={(e) => {
                  setBatchAction(e.target.value);
                  setBatchId(crypto.randomUUID());
                }}
              >
                <option value="POSTPONE">Postpone</option>
                <option value="WRITE_OFF">Write off</option>
              </select>
            </label>
            <label>
              Batch rationale
              <input
                aria-label="Batch rationale"
                value={batchReason}
                onChange={(e) => {
                  setBatchReason(e.target.value);
                  setBatchId(crypto.randomUUID());
                }}
              />
            </label>
            <p>
              The selected entries and their full remaining amounts will be
              adjusted together.
            </p>
            <button
              disabled={
                busy ||
                !selectedLines.length ||
                !batchReason.trim() ||
                data?.project?.id !== id
              }
              onClick={() =>
                go(async () => {
                  await request(`/api/projects/${id}/adjustments/bulk`, {
                    method: "POST",
                    body: JSON.stringify({
                      batchId,
                      action: batchAction,
                      actor: data?.project?.requestor,
                      reason: batchReason,
                      confirm: true,
                      stateHash: data?.financialHash,
                      items: data?.position?.lines
                        ?.filter((a: any) => selectedLines.includes(a.lineId))
                        .map((a: any) => ({
                          lineId: a.lineId,
                          amount: a.bill,
                        })),
                    }),
                  });
                  setData(await request(`/api/projects/${id}/wip`));
                  setHistory(await request(`/api/projects/${id}/adjustments`));
                  setSelectedLines([]);
                  setBatchReason("");
                  setBatchId(crypto.randomUUID());
                  setCommercial(null);
                  setReview(null);
                  setPbd(null);
                })
              }
            >
              Confirm selected adjustments
            </button>
            <h3>Adjust selected line</h3>
            <label>Line</label>
            <select
              value={adjustmentLine}
              onChange={(e) => {
                setAdjustmentLine(e.target.value);
                setAdjustmentEvent(crypto.randomUUID());
              }}
            >
              <option value="">Select a WIP line</option>
              {(data?.lines || []).map((line: any) => (
                <option key={line.id} value={line.id}>
                  {line.id} · {line.person}
                </option>
              ))}
            </select>
            <label>Action</label>
            <select
              value={adjustmentAction}
              onChange={(e) => {
                setAdjustmentAction(e.target.value);
                setAdjustmentEvent(crypto.randomUUID());
              }}
            >
              <option value="POSTPONE">Postpone</option>
              <option value="WRITE_OFF">Write off</option>
            </select>
            <label>Amount in project currency</label>
            <input
              type="number"
              min="0.01"
              step="0.01"
              value={adjustmentAmount}
              onChange={(e) => {
                setAdjustmentAmount(e.target.value);
                setAdjustmentEvent(crypto.randomUUID());
              }}
            />
            <label>Reason</label>
            <input
              value={adjustmentReason}
              onChange={(e) => {
                setAdjustmentReason(e.target.value);
                setAdjustmentEvent(crypto.randomUUID());
              }}
            />
            <button
              disabled={
                busy ||
                !adjustmentLine ||
                !adjustmentAmount ||
                !adjustmentReason
              }
              onClick={() =>
                go(async () => {
                  await request(`/api/projects/${id}/adjustments`, {
                    method: "POST",
                    body: JSON.stringify({
                      lineId: adjustmentLine,
                      action: adjustmentAction,
                      amount: Number(adjustmentAmount),
                      reason: adjustmentReason,
                      actor: data?.project?.requestor,
                      confirm: true,
                      eventId: adjustmentEvent,
                    }),
                  });
                  setData(await request(`/api/projects/${id}/wip`));
                  setHistory(await request(`/api/projects/${id}/adjustments`));
                  setCommercial(null);
                  setPbd(null);
                  setReview(null);
                  setAdjustmentAmount("");
                  setAdjustmentReason("");
                  setAdjustmentEvent(crypto.randomUUID());
                })
              }
            >
              Confirm adjustment
            </button>
            <button
              disabled={busy}
              onClick={() =>
                go(async () =>
                  setHistory(await request(`/api/projects/${id}/adjustments`)),
                )
              }
            >
              Load adjustment history
            </button>
            {history.map((ev) => (
              <div className="notice" key={ev.eventId}>
                <b>
                  {ev.action} · {ev.lineId} · {ev.amount}
                </b>
                <p>{ev.reason}</p>
                <small>
                  {ev.actor} · {ev.at} · Remaining: {ev.before.bill} →{" "}
                  {ev.after.bill}
                </small>
              </div>
            ))}
          </fieldset>
        </section>
        <div className="details-grid">
          <section className="panel commercial-panel">
            <fieldset disabled={busy || !projectReady}>
              <h2>Currency and VAT preview</h2>
              <button
                disabled={busy}
                onClick={() =>
                  go(async () =>
                    setCommercial(
                      await request(`/api/projects/${id}/commercial-preview`),
                    ),
                  )
                }
              >
                Calculate current preview
              </button>
              {commercial && (
                <div className="notice">
                  <p>
                    Net: {commercial.preview.billingNet}{" "}
                    {commercial.preview.billingCurrency} · VAT:{" "}
                    {commercial.preview.vat} · Gross: {commercial.preview.gross}
                  </p>
                  <p>
                    {commercial.preview.exchangeRate.source} · Rate:{" "}
                    {commercial.preview.exchangeRate.rate} · Date:{" "}
                    {commercial.preview.exchangeRate.asOf}
                  </p>
                  <small>{commercial.draftIntegration}</small>
                </div>
              )}
              <div className="divider" />
              <h2>Write-up</h2>
              <label>Additional bill amount (project currency)</label>
              <input
                type="number"
                min="0.01"
                step="0.01"
                value={writeUpAmount}
                onChange={(e) => {
                  setWriteUpAmount(e.target.value);
                  setWriteUpEvent(crypto.randomUUID());
                }}
              />
              <label>Reason</label>
              <input
                value={writeUpReason}
                onChange={(e) => {
                  setWriteUpReason(e.target.value);
                  setWriteUpEvent(crypto.randomUUID());
                }}
              />
              <button
                disabled={busy || !writeUpAmount || !writeUpReason}
                onClick={() =>
                  go(async () => {
                    await request(`/api/projects/${id}/write-ups`, {
                      method: "POST",
                      body: JSON.stringify({
                        eventId: writeUpEvent,
                        amount: Number(writeUpAmount),
                        actor: data?.project?.requestor,
                        reason: writeUpReason,
                        confirm: true,
                      }),
                    });
                    setData(await request(`/api/projects/${id}/wip`));
                    setCommercial(null);
                    setPbd(null);
                    setReview(null);
                    setWriteUpAmount("");
                    setWriteUpReason("");
                    setWriteUpEvent(crypto.randomUUID());
                  })
                }
              >
                Confirm write-up
              </button>
            </fieldset>
          </section>
          <section className="panel document-panel" id="document">
            <div className="section-title">
              <div>
                <span className="eyebrow">FINAL REVIEW</span>
                <h2>Simulated SAP document</h2>
                <p>Create, approve and track your exact billing version.</p>
              </div>
              <Icon name="file" />
            </div>
            <fieldset disabled={busy || !projectReady}>
              <label>
                <input
                  type="checkbox"
                  checked={vatConfirmed}
                  onChange={(e) => setVatConfirmed(e.target.checked)}
                />{" "}
                I have reviewed the VAT treatment
              </label>
              {data?.project?.currency !== data?.project?.billingCurrency && (
                <label>
                  <input
                    type="checkbox"
                    checked={currencyConfirmed}
                    onChange={(e) => setCurrencyConfirmed(e.target.checked)}
                  />{" "}
                  I confirm the alternate billing currency and synthetic
                  conversion policy
                </label>
              )}
              <p className="muted">
                Only projects without blocking findings can create a PBD.
              </p>
              <button
                disabled={busy}
                onClick={() =>
                  go(async () =>
                    setPbd(
                      await request(`/api/projects/${id}/pbd`, {
                        method: "POST",
                        body: JSON.stringify({
                          actor: data?.project?.requestor,
                          confirm: true,
                          narrative: "Synthetic billing demonstration",
                          vatConfirmed,
                          currencyConfirmed,
                          acknowledgements: (data?.findings || [])
                            .filter(
                              (f: Finding) =>
                                f.severity === "CONFIRM" &&
                                acknowledged.includes(f.dependencyHash),
                            )
                            .map((f: Finding) => ({
                              findingId: f.id,
                              dependencyHash: f.dependencyHash,
                            })),
                          idempotencyKey: `demo-${id}-${data?.financialHash}`,
                        }),
                      }),
                    ),
                  )
                }
              >
                Create simulated PBD
              </button>
              {pbd && (
                <div className="notice">
                  <b>{pbd.id}</b>
                  <p>
                    Status: {pbd.status} · Version: {pbd.version}
                  </p>
                  <p>
                    Net: {pbd.amount.toLocaleString("en-GB")} {pbd.currency} ·
                    VAT: {pbd.commercial?.vat} · Gross: {pbd.commercial?.gross}
                  </p>
                  <div className="buttons">
                    <button
                      disabled={busy || pbd.status !== "DRAFT"}
                      onClick={() =>
                        go(async () =>
                          setPbd(
                            await request(`/api/pbd/${pbd.id}/approve`, {
                              method: "POST",
                              body: JSON.stringify({
                                actor: data?.project?.requestor,
                                confirm: true,
                                stateHash: pbd.stateHash,
                                version: pbd.version,
                              }),
                            }),
                          ),
                        )
                      }
                    >
                      Approve exact version
                    </button>
                    <button
                      disabled={busy || pbd.status !== "APPROVED"}
                      onClick={() =>
                        go(async () =>
                          setPbd(
                            await request(`/api/pbd/${pbd.id}/post`, {
                              method: "POST",
                              body: JSON.stringify({
                                actor: data?.project?.requestor,
                                confirm: true,
                              }),
                            }),
                          ),
                        )
                      }
                    >
                      Confirm posting
                    </button>
                    <button
                      disabled={busy}
                      onClick={() =>
                        go(async () =>
                          setPbd(await request(`/api/pbd/${pbd.id}`)),
                        )
                      }
                    >
                      Refresh posting status
                    </button>
                  </div>
                  {pbd.status === "POST_PENDING" && (
                    <p>
                      Posting is queued. Refresh to check the worker result.
                    </p>
                  )}
                  {pbd.mockSapPostingId && (
                    <p>Mock SAP posting ID: {pbd.mockSapPostingId}</p>
                  )}
                </div>
              )}
            </fieldset>
          </section>
        </div>
        <footer>
          LedgerPilot · Synthetic data and simulated SAP. Do not enter real
          financial or client information.
        </footer>
      </main>
    </div>
  );
}
createRoot(document.getElementById("root")!).render(<App />);
