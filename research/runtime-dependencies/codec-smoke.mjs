// Development-only reproducibility helper; production uses codec-bridge.mjs.
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { encode, decode, getDuration } from './silk-wasm/lib/index.mjs';

let pcm, encoded, decoded;
try {
  if (process.argv.length !== 4) throw new Error('invalid arguments');
  pcm = await readFile(process.argv[2]);
  const result = await encode(pcm, 24000);
  encoded = result.data;
  if (encoded[0] !== 2 || Buffer.from(encoded.subarray(1, 10)).toString('ascii') !== '#!SILK_V3') throw new Error('unexpected SILK header');
  decoded = (await decode(encoded, 24000)).data;
  await writeFile(process.argv[3], encoded, { flag: 'wx' });
  process.stdout.write(JSON.stringify({ inputBytes: pcm.length, silkBytes: encoded.length, decodedPcmBytes: decoded.length,
    durationMilliseconds: result.duration, parsedDurationMilliseconds: getDuration(encoded),
    tencentHeader: true, sha256: createHash('sha256').update(encoded).digest('hex') }) + '\n');
} catch {
  process.stderr.write('Voice codec smoke test failed.\n');
  process.exitCode = 1;
} finally {
  pcm?.fill(0); encoded?.fill(0); decoded?.fill(0);
}
