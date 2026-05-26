import { createReadStream, existsSync, mkdirSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { basename, extname, join, normalize, resolve, sep } from "node:path";

const root = resolve(import.meta.dirname);
const repoRoot = resolve(root, "..", "..");
const renderRoot = resolve(repoRoot, "artifacts", "vocal-tract-playground");
const rendererProject = resolve(repoRoot, "tools", "TractGraphRenderer", "TractGraphRenderer.csproj");
const port = Number.parseInt(process.env.PORT ?? "5125", 10);
const host = process.env.HOST ?? "127.0.0.1";

const types = new Map([
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".wav", "audio/wav"]
]);

mkdirSync(renderRoot, { recursive: true });

function fileFor(url) {
  const requestPath = decodeURIComponent(new URL(url, `http://${host}:${port}`).pathname);
  if (requestPath.startsWith("/renders/")) {
    const file = resolve(join(renderRoot, basename(requestPath)));
    return file === renderRoot || file.startsWith(renderRoot + sep) ? file : "";
  }

  const normalized = normalize(requestPath === "/" ? "/index.html" : requestPath);
  const file = resolve(join(root, normalized));
  return file === root || file.startsWith(root + sep) ? file : "";
}

function readBody(request) {
  return new Promise((resolveBody, reject) => {
    let body = "";
    request.setEncoding("utf8");
    request.on("data", chunk => {
      body += chunk;
      if (body.length > 24_000) {
        reject(new Error("request body too large"));
        request.destroy();
      }
    });
    request.on("end", () => resolveBody(body));
    request.on("error", reject);
  });
}

function renderGraph(body) {
  return new Promise((resolveRender, reject) => {
    const child = spawn("dotnet", [
      "run",
      "--project",
      rendererProject,
      "-p:UseSharedCompilation=false",
      "-p:BuildInParallel=false"
    ], {
      cwd: repoRoot,
      stdio: ["pipe", "pipe", "pipe"]
    });

    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", chunk => { stdout += chunk; });
    child.stderr.on("data", chunk => { stderr += chunk; });
    child.on("error", reject);
    child.on("close", code => {
      if (code !== 0) {
        reject(new Error(stderr || stdout || `renderer exited ${code}`));
        return;
      }

      try {
        resolveRender(JSON.parse(stdout));
      } catch (error) {
        reject(new Error(`renderer returned invalid JSON: ${error.message}\n${stdout}`));
      }
    });
    child.stdin.end(JSON.stringify(body));
  });
}

async function handleRender(request, response) {
  try {
    const controls = JSON.parse(await readBody(request));
    const fileName = `tract-${Date.now()}-${Math.random().toString(16).slice(2)}.wav`;
    const outputPath = resolve(join(renderRoot, fileName));
    const summary = await renderGraph({
      ...controls,
      outputPath,
      sampleRate: controls.sampleRate ?? 44100,
      durationSeconds: controls.durationSeconds ?? 0.57
    });

    response.writeHead(200, {
      "cache-control": "no-store",
      "content-type": "application/json; charset=utf-8"
    });
    response.end(JSON.stringify({
      ...summary,
      audioUrl: `/renders/${fileName}`
    }));
  } catch (error) {
    response.writeHead(500, {
      "cache-control": "no-store",
      "content-type": "application/json; charset=utf-8"
    });
    response.end(JSON.stringify({ error: error.message }));
  }
}

createServer((request, response) => {
  if (request.method === "POST" && new URL(request.url ?? "/", `http://${host}:${port}`).pathname === "/render") {
    void handleRender(request, response);
    return;
  }

  const file = fileFor(request.url ?? "/");
  if (!file || !existsSync(file) || !statSync(file).isFile()) {
    response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
    response.end("not found");
    return;
  }

  response.writeHead(200, {
    "cache-control": "no-store",
    "content-type": types.get(extname(file)) ?? "application/octet-stream"
  });
  createReadStream(file).pipe(response);
}).listen(port, host, () => {
  console.log(`AquaSynth vocal tract playground: http://${host}:${port}/`);
});
