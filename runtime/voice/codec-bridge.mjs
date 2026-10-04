// Fixed binary IPC bridge. No user code, shell command, URL, filename, or module name is accepted.
// Codec: verified npm silk-wasm 3.7.1. Tencent-format output begins with 02 + "#!SILK_V3".
console.log = () => {};
console.error = () => {};
const MAX_BYTES = 100 * 1024 * 1024;
const HEADER_BYTES = 28;
let chunks = [], request, input, output, decodeInput;
try {
  const { encode, decode, getDuration } = await import('./silk-wasm/lib/index.mjs');
  let count = 0;
  for await (const chunk of process.stdin) {
    count += chunk.length;
    if (count > MAX_BYTES + HEADER_BYTES) throw new Error('request limit');
    chunks.push(chunk);
  }
  request = Buffer.concat(chunks, count);
  for (const chunk of chunks) chunk.fill(0);
  chunks = [];
  if (request.length < HEADER_BYTES || request.subarray(0, 4).toString('ascii') !== 'WXC1' || request.readUInt32LE(4) !== 1) throw new Error('request header');
  const mode = request.readUInt32LE(8), rate = request.readUInt32LE(12), length = request.readUInt32LE(16);
  const maxDuration = request.readUInt32LE(20), maxOutput = request.readUInt32LE(24);
  if (![8000, 12000, 16000, 24000, 32000, 44100, 48000].includes(rate) || ![1, 2].includes(mode) ||
      length === 0 || length > MAX_BYTES || request.length !== length + HEADER_BYTES ||
      maxDuration < 20 || maxDuration > 3600000 || maxOutput < 1 || maxOutput > MAX_BYTES) throw new Error('request parameters');
  input = request.subarray(HEADER_BYTES);
  let result;
  if (mode === 1) {
    if (input.length % 2 !== 0 || input.length < rate * 2 / 50 || input.length * 1000 > maxDuration * rate * 2) throw new Error('PCM limit');
    // The public encode API auto-detects WAV. IPC mode 1 is strictly raw PCM;
    // containers have already been validated/extracted by the C# WAV entry point.
    if (input.length >= 12 && input.subarray(0, 4).toString('ascii') === 'RIFF' && input.subarray(8, 12).toString('ascii') === 'WAVE') throw new Error('PCM container');
    result = await encode(input, rate);
    if (result.data[0] !== 2 || Buffer.from(result.data.subarray(1, 10)).toString('ascii') !== '#!SILK_V3') throw new Error('encoded header');
  } else {
    let offset;
    if (input[0] === 2 && input.length >= 10 && input.subarray(1, 10).toString('ascii') === '#!SILK_V3') offset = 10;
    else throw new Error('SILK header');
    let frames = 0, terminal = false;
    while (offset < input.length) {
      if (offset + 2 > input.length) throw new Error('frame prefix');
      const size = input.readUInt16LE(offset); offset += 2;
      if (size === 65535 && offset === input.length) { terminal = true; break; }
      // common.h MAX_BYTES_PER_FRAME(250) * MAX_INPUT_FRAMES(5).
      if (size === 0 || size > 1250 || size > input.length - offset || ++frames * 20 > maxDuration) throw new Error('frame limit');
      offset += size;
    }
    if (frames < 2) throw new Error('decoder lookahead');
    // Duration is parsed from the encoded frames before the SDK can allocate decoded PCM.
    const duration = getDuration(terminal ? input.subarray(0, input.length - 2) : input);
    if (duration !== frames * 20 || duration > maxDuration || duration * rate * 2 / 1000 * 5 > maxOutput) throw new Error('decoded output limit');
    // decoder.c reads a packet prefix before checking EOF. Supply an explicit
    // terminating signed -1 so the pinned C decoder never reads past our bytes.
    decodeInput = terminal ? Buffer.from(input) : Buffer.concat([input, Buffer.from([255, 255])]);
    result = await decode(decodeInput, rate);
    // getDuration defaults to 20ms per packet; incoming packets can hold 1..5
    // frames. Actual PCM byte count, rather than packet count or Playtime, is authoritative.
    const actualDuration = result.data.length * 1000 / (rate * 2);
    if (!Number.isInteger(actualDuration) || actualDuration < duration) throw new Error('PCM duration');
    result.duration = actualDuration;
  }
  output = result.data;
  if (!Number.isInteger(result.duration) || result.duration <= 0 || result.duration > maxDuration ||
      output.length === 0 || output.length > maxOutput) throw new Error('codec result limit');
  if (mode === 2 && (output.length % 2 !== 0 || output.length !== result.duration * rate * 2 / 1000)) throw new Error('PCM result');
  if (mode === 1 && getDuration(output) !== result.duration) throw new Error('SILK duration');
  const header = Buffer.alloc(16);
  header.write('WXR1', 0, 'ascii'); header.writeUInt32LE(1, 4);
  header.writeUInt32LE(result.duration, 8); header.writeUInt32LE(output.length, 12);
  await new Promise((resolve, reject) => process.stdout.write(header, err => err ? reject(err) : resolve()));
  await new Promise((resolve, reject) => process.stdout.write(output, err => err ? reject(err) : resolve()));
  header.fill(0);
} catch {
  process.stderr.write('Voice codec operation failed.\n');
  process.exitCode = 1;
} finally {
  for (const chunk of chunks) chunk.fill(0);
  input?.fill(0); request?.fill(0); output?.fill(0); decodeInput?.fill(0);
}
