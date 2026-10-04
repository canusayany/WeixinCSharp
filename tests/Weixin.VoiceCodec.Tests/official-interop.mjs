import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
const [modulePath, pcmPath, silkPath, wavePath] = process.argv.slice(2);
let pcm, silk, wave, encoded, decoded;
try {
  const { encode, decode, getDuration } = await import(pathToFileURL(modulePath).href);
  pcm = await readFile(pcmPath); silk = await readFile(silkPath); wave = await readFile(wavePath);
  encoded = await encode(pcm, 24000);
  const terminated = Buffer.concat([silk, Buffer.from([255, 255])]);
  try { decoded = await decode(terminated, 24000); } finally { terminated.fill(0); }
  const equalEncode = Buffer.from(encoded.data).equals(silk);
  const equalDecode = Buffer.from(decoded.data).equals(wave.subarray(44));
  if (!equalEncode || !equalDecode || encoded.duration !== 2000 || getDuration(silk) !== 2000) throw new Error();
  process.stdout.write(JSON.stringify({ equalEncode, equalDecode, duration: encoded.duration,
    pcmBytes: decoded.data.length, silkBytes: silk.length,
    silkSha256: createHash('sha256').update(silk).digest('hex') }));
} catch { process.stderr.write('Independent official codec interoperability failed.\n'); process.exitCode = 1; }
finally { pcm?.fill(0); silk?.fill(0); wave?.fill(0); encoded?.data.fill(0); decoded?.data.fill(0); }
