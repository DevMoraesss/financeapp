#!/usr/bin/env node
/*
 * Troca caracteres tipograficos por ASCII simples em todo o repositorio.
 *
 * Motivo: travessao, setas, aspas curvas, checkmarks e emoji deixam o texto com cara de gerado
 * por maquina. O projeto usa apenas ASCII para pontuacao e simbolos; acentos do portugues
 * continuam normalmente (a crase, o til, a cedilha).
 *
 * Uso:
 *   node tools/clean-chars.mjs           corrige
 *   node tools/clean-chars.mjs --check   so verifica; sai com codigo 1 se achar algo (para o CI)
 *
 * Detalhe importante: as regras usam escapes \\uXXXX em vez dos caracteres literais. Assim este
 * arquivo e 100% ASCII e o script nunca corrompe a si mesmo ao rodar sobre o proprio codigo.
 */

import { readFileSync, writeFileSync } from 'node:fs'
import { readdir, stat } from 'node:fs/promises'
import { join, extname, resolve } from 'node:path'

const ROOT = resolve(new URL('..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'))
const SELF = resolve(new URL('', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'))

const EXTENSIONS = new Set([
  '.md', '.cs', '.ts', '.tsx', '.js', '.jsx', '.mjs', '.json', '.jsonc',
  '.yml', '.yaml', '.css', '.html', '.csproj', '.props', '.slnx', '.editorconfig', '.gitattributes',
])

const SKIP_DIRS = new Set(['node_modules', 'bin', 'obj', '.git', 'dist', '.vs', 'TestResults'])

/** Pares [regex, substituto]. Ordem importa: o mais especifico vem primeiro. */
const RULES = [
  // Tracos
  [/—/g, '-'],   // travessao (em dash)
  [/–/g, '-'],   // meia risca (en dash)
  [/−/g, '-'],   // sinal de menos
  [/‑/g, '-'],   // hifen sem quebra

  // Setas
  [/→/g, '->'],
  [/←/g, '<-'],
  [/↔/g, '<->'],
  [/⇒/g, '=>'],
  [/↑/g, '+'],
  [/↓/g, '-'],
  [/➜|➔|➡/g, '->'],

  // Marcadores de status
  [/✓|✔|✅/g, 'sim'],
  [/✗|✘|❌/g, 'nao'],
  [/⏳/g, 'pendente'],
  [/⚠️?/g, 'atencao'],

  // Pontuacao
  [/•/g, '-'],
  [/…/g, '...'],
  [/[“”]/g, '"'],
  [/[‘’]/g, "'"],
  [/′/g, "'"],
  [/″/g, '"'],
  [/ /g, ' '],
  [/[​‌‍﻿]/g, ''],

  // Matematica
  [/×/g, 'x'],
  [/÷/g, '/'],
  [/≤/g, '<='],
  [/≥/g, '>='],
  [/≠/g, '!='],
  [/Σ/g, 'soma'],

  // Sinal de secao
  [/§\s?/g, 'secao '],

  // Desenho de caixa (arvores de diretorio)
  [/├──/g, '+--'],
  [/└──/g, '\\--'],
  [/│/g, '|'],
  [/[┌┐└┘├┤┬┴┼]/g, '+'],
  [/─/g, '-'],

  // Emoji e simbolos decorativos
  [/[\u{1f300}-\u{1faff}]/gu, ''],
  [/[\u{2600}-\u{27bf}]/gu, ''],
  [/️/g, ''],
]

async function collect(dir, files = []) {
  for (const entry of await readdir(dir)) {
    if (SKIP_DIRS.has(entry)) continue

    const full = join(dir, entry)
    const info = await stat(full)

    if (info.isDirectory()) {
      await collect(full, files)
    } else if (EXTENSIONS.has(extname(entry)) || entry === '.editorconfig' || entry === '.gitattributes') {
      files.push(full)
    }
  }

  return files
}

const checkOnly = process.argv.includes('--check')
const files = (await collect(ROOT)).filter((file) => resolve(file) !== SELF)
const touched = []

for (const file of files) {
  const original = readFileSync(file, 'utf8')
  let cleaned = original

  for (const [pattern, replacement] of RULES) {
    cleaned = cleaned.replace(pattern, replacement)
  }

  if (cleaned !== original) {
    touched.push(file.slice(ROOT.length + 1))
    if (!checkOnly) writeFileSync(file, cleaned, 'utf8')
  }
}

if (touched.length === 0) {
  console.log(`Nenhum caractere tipografico em ${files.length} arquivos.`)
  process.exit(0)
}

console.log(`${touched.length} de ${files.length} arquivo(s) ${checkOnly ? 'com pendencia' : 'corrigido(s)'}:`)
for (const file of touched) console.log('  ' + file)
process.exit(checkOnly ? 1 : 0)
