/** Create a browser-readable preview without changing the file used for OCR or saving. */
export async function receiptPreview(sourceUrl: string, signal: AbortSignal): Promise<Blob> {
  const response = await fetch(sourceUrl, { signal })
  if (!response.ok) throw new Error('Nie można pobrać paragonu.')
  const blob = await response.blob()
  signal.throwIfAborted()
  const { isHeic, heicTo } = await import('heic-to/csp')
  // Saved receipts have no MIME type in the API response, so inspect the actual file.
  if (!await isHeic(new File([blob], 'paragon', { type: blob.type }))) {
    throw new Error('Nie można wyświetlić tego formatu paragonu.')
  }
  signal.throwIfAborted()
  const preview = await heicTo({ blob, type: 'image/jpeg', quality: 0.9 })
  signal.throwIfAborted()
  return preview
}
