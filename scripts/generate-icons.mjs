import { deflateSync } from 'node:zlib';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const outputDirectory = resolve('public/icons');
mkdirSync(outputDirectory, { recursive: true });

const crcTable = Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit += 1) {
    value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  }
  return value >>> 0;
});

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const typeBuffer = Buffer.from(type);
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const checksum = Buffer.alloc(4);
  checksum.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])));
  return Buffer.concat([length, typeBuffer, data, checksum]);
}

function makeIcon(size) {
  const rows = [];
  const scale = size / 128;
  for (let y = 0; y < size; y += 1) {
    const row = Buffer.alloc(1 + size * 4);
    for (let x = 0; x < size; x += 1) {
      const px = x / scale;
      const py = y / scale;
      const centerLine = Math.abs(px - 64) < 10 && py > 48 && py < 82;
      const curve = ((px - 64) ** 2 + (py - 40) ** 2 < 25 ** 2) && py < 49 && px > 57;
      const cutout = ((px - 64) ** 2 + (py - 40) ** 2 < 10 ** 2) && py < 47;
      const dot = (px - 64) ** 2 + (py - 101) ** 2 < 9 ** 2;
      const question = (centerLine || curve || dot) && !cutout;
      const offset = 1 + x * 4;
      row[offset] = question ? 202 : 255;
      row[offset + 1] = question ? 44 : 255;
      row[offset + 2] = question ? 52 : 255;
      row[offset + 3] = question ? 255 : 0;
    }
    rows.push(row);
  }

  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8;
  header[9] = 6;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(Buffer.concat(rows))),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

for (const size of [16, 32, 48, 128]) {
  writeFileSync(resolve(outputDirectory, `icon${size}.png`), makeIcon(size));
}
