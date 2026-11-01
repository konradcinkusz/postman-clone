# User guide

This guide is for people who use Postman Clone to call web services and share their requests with a team. You do not need to
know how it is built. Where a step needs a program on your computer, it says so, and what it does.

> **Status.** Signing in, collections, environments, history and the local agent are on the Web. The agent is a program you
> install on your own computer; sections 4 to 6 describe it. Who gives you the agent's package is decided by your team's owner.

**Contents**

1. Open the app and sign in
2. Make a collection and send a request
3. Share a collection with your team
4. Install the agent
5. Pair the agent with your account
6. Send a request to `localhost`
7. What the agent can see, and what is stored where
8. Stop using the agent
9. Questions and problems

---

## 1. Open the app and sign in

Open the address your owner gave you. By default it is `https://postmanclone-web.fly.dev`. The address starts with `https://`;
if your browser shows a warning about it, do not continue, and tell your owner.

- **Create an account:** from the sign-in page, choose to create an account, give your email address and a password. The password rules are shown on the form.
- **Sign in:** use the same email and password.
- **Sign out:** use *Sign out* in the top bar. It ends your session on the server and in your browser.

The app keeps you signed in for a limited time. After a long quiet period you are asked to sign in again. A new version of the
app also signs everyone out, once.

## 2. Make a collection and send a request

A **collection** is a named group of requests. A **folder** groups requests inside a collection. A **request** is one call: its
method (GET, POST and so on), its address, its headers and its body.

1. In the left panel, choose *New collection* and give it a name.
2. Choose *New request* in the collection and type the address, for example `https://api.example.com/items`.
3. Choose *Send*. The response appears below: its status, its headers and its body.

A response body that is very large is cut off at a limit, and the panel says so. The app never shows you more than the limit.

**Environments** keep values you use in many requests, such as the address of a test server. Put the value in an environment
variable, for example `{{base_url}}`, and choose the environment in the top bar. A variable marked as a **secret** is hidden: you
can type it, you can change it, but nobody can read it back on the Web, not even you. The request history shows `••••••` where a
secret was.

## 3. Share a collection with your team

Collections belong to a **workspace**. Everyone who is a member of the workspace sees its collections, environments and history.

1. Open *Members of this workspace*.
2. Enter a teammate's email address and send an invitation. The invitation has a link that expires.
3. Your teammate opens the link, signs in or registers with that email address, and accepts. They then see the workspace.

Only invite people who should see every collection and environment in the workspace. A member with access to an environment
can also read its secret values, when they send a request that uses them. That is by design, and it is why secrets are not shown
on the Web.

To give a team a separate space, create a second workspace. Collections do not move between workspaces by themselves: export a
collection from one (*Export as Postman*, a Postman v2.1 file) and import it into the other (*Import file*).

## 4. Install the agent

The **agent** is a small program that runs on your computer. Some requests can only be sent from your computer: a request to
`localhost`, to another machine on your network, or to a service that runs inside your office. The shared server cannot reach
those, by design. The agent can.

You install it **once**, and you do not write code. Your team provides the agent's package; the owner decides where it is
published (it is not on the public NuGet feed). The **Agent** page of the app shows the install command. It is the same on
Windows, macOS and Linux:

```
dotnet tool install --global postmanclone-agent --add-source <the folder that holds the package>
```

Then tell the agent two things, once, with the setup command the Agent page shows:

- the **server address**: the address of the PostmanClone server, which your owner gives you;
- the **page address**: the address of the page in your browser, which the Agent page fills in for you.

```
postmanclone-agent setup --server <the server address> --web-origin <the page address>
```

Start the agent and keep it running while you use PostmanClone:

```
postmanclone-agent run
```

The agent listens on your computer only (`127.0.0.1`, port `47631`). No other computer can reach it. It accepts requests only from
the Postman Clone page in your browser, and only after you pair it.

To remove the agent later, run `postmanclone-agent forget` on that computer, then revoke it in the list on the Agent page.

## 5. Pair the agent with your account

Pairing tells the agent which account it works for. You do it once per computer, and again after you reload the page (see below).

1. On the **Agent** page, choose *Create pairing code*. A code of ten characters appears with a five-minute timer.
2. Choose *Pair this page*. The page sends the code to the agent on your computer, the agent exchanges it with the server, and the
   page receives a token that only your agent accepts. Use the code here; do not type it in a terminal, because a code works once.
3. The top bar shows **Connected**. The Agent page lists the agent under *Your agents in this workspace*.

The code works once and only for five minutes. If it expires, create a new one.

The top bar shows what the page can do:

- **Connected**: the agent is running and this page is paired with it.
- **Not running**: no agent answers on this computer. Start it (section 4), then choose *Check again* on the Agent page.
- **Not paired**: the agent is running but has not been set up or paired for this page. Check the setup, then pair.
- **Pair again**: the agent is paired, but this page does not hold its token. This happens after you reload the page or open it
  again, because the page keeps its token in memory only and never stores it. Create a new code and pair this page.

## 6. Send a request to `localhost`

1. Start the program or service you want to call, on your computer, for example on `http://localhost:3000`.
2. In the request builder, type the address, `http://localhost:3000/health`.
3. Look at the **Send with** control. It says **Agent (this computer)** when this page is paired with the agent, which is the
   default then. It says **Server** otherwise. You can choose either. Choose *Server* for a public address you want the shared
   server to send.
4. Choose *Send*. The response panel says which sender answered: *Sent by your agent on this computer*, or *Sent by the server*.

A request to a public address (for example `https://api.example.com`) can also be sent by the server. A request to `localhost`
sent through the server is refused, with the code *ssrf_blocked_address*. That is correct: the server refuses to call machines inside
its own network. Use the agent for those.

**Check the address before you send.** A collection that a teammate shared can point at any address, including a machine on your
own network. Read the address before you choose *Send*, most of all if the request carries an environment secret.

## 7. What the agent can see, and what is stored where

This section says what happens to your data. It is the plain account of the design; the detail is in the developer documents.

**On the shared server (Fly.io, in the region your owner chose):**

- Your account (email address, a password stored only in scrambled form), your workspaces, your collections, requests,
  environments and variables.
- Environment **secret values**, encrypted on the disk. The Web never shows them back.
- The **history** of your sends: the request as a template, with secrets masked, and the response, kept up to 200 entries per user
  and workspace. The oldest is removed when a new one is added.
- The agent's own record: which computers you paired, when each was last used, and when you revoked one. The agent's token is
  stored on the server only as a scrambled value.

**In your browser:**

- A session cookie that keeps you signed in. The app's pages do not keep your password or the agent's pairing code after you
  leave them.
- The pairing token of the agent lives in the page's memory only. It is gone when you close the page.

**On your computer, with the agent:**

- The agent's configuration file, with the **agent token**. The file is readable by your user account only. This token is a
  credential: anyone who has the file can send requests as you, through your agent, until you revoke it.
- During a send, the agent asks the server for the environment's secret values **and holds them in memory for that one send**.
  It does not write them to disk, and it does not show them in its output or in the history. The request is still sent to the
  address you chose, with the secret in it, as you asked.

**The trade-off you accept by using the agent:** an environment secret is sent to your computer for the length of a send. Anyone
who could read a secret on the Web could already read it, so the risk is the place it passes through, not a new reader. If you do
not want a secret on your computer, do not send that request through the agent; choose *server* for a public address, or do not
use that environment on your computer.

## 8. Stop using the agent

On the **Agent** page, choose *Revoke* next to the computer. The agent token stops working at once, and the next send shows
**Pair again**. Then delete the agent's configuration file, or run the *forget* command the Agent page shows. Revoke an agent you
lost or no longer use, even if you did not lose the file.

If you leave the team, ask your owner to remove your membership of the workspace. Revoking an agent is not done automatically when a
membership ends; it is your owner's decision, and it is open. Revoke your agents yourself first.

## 9. Questions and problems

- **The page does not load or shows a server error.** Wait a minute and reload. If it still fails, tell your owner the time and the
  address you used.
- **Every page asks you to sign in again.** Your session ended, or the app was updated. Sign in again.
- **Sign-in says "too many attempts".** Wait a minute. The limit is shared between people who use the same address, so a team may
  see it sooner than expected. Tell your owner if it happens often.
- **A send fails with an error about the network.** Check the address and that the service is running. For a `localhost` address,
  check that the agent is connected.
- **Your owner asks about the agent's token or a lost computer.** Revoke that agent on the Agent page (section 8).
