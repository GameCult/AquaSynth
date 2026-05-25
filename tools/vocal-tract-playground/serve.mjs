import { createReadStream, existsSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize, resolve, sep } from "node:path";

const root = resolve(import.meta.dirname);
const port = Number.parseInt(process.env.PORT ?? "5125", 10);
const host = process.env.HOST ?? "127.0.0.1";

const types = new Map([
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"]
]);

function fileFor(url) {
  const requestPath = decodeURIComponent(new URL(url, `http://${host}:${port}`).pathname);
  const normalized = normalize(requestPath === "/" ? "/index.html" : requestPath);
  const file = resolve(join(root, normalized));
  return file === root || file.startsWith(root + sep) ? file : "";
}

createServer((request, response) => {
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
