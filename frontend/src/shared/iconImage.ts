import { fileTooLarge } from './fileSize'

const MAX_SOURCE_BYTES = 25 * 1024 * 1024
const MAX_UPLOAD_BYTES = 5 * 1024 * 1024
export const ICON_TOO_LARGE = fileTooLarge(MAX_UPLOAD_BYTES, 'grafiki')
const MAX_SIDE = 512
export const ICON_IMAGE_ACCEPT = 'image/png,image/jpeg,image/webp,image/heic,image/heif,.heic,.heif'

/** Decode locally (including iPhone HEIC), apply orientation and fit inside 512 × 512. */
export async function prepareIconImage(file: File): Promise<File> {
  if (!file.size) throw new Error('Wybrany plik jest pusty. Wybierz inny plik.')
  if (file.size > MAX_SOURCE_BYTES) throw new Error(fileTooLarge(MAX_SOURCE_BYTES, 'grafiki'))
  const heic = /\.(heic|heif)$/i.test(file.name) || /^image\/hei[cf](-sequence)?$/i.test(file.type)
  if (!heic && !['image/png', 'image/jpeg', 'image/webp'].includes(file.type) && !(file.type === '' && /\.(png|jpe?g|webp)$/i.test(file.name))) {
    throw new Error('Wybierz obsługiwany format pliku: JPEG, PNG, WebP, HEIC/HEIF.')
  }

  let source: HTMLImageElement | ImageBitmap | null = null
  let objectUrl: string | null = null
  const canvas = document.createElement('canvas')
  try {
    if (heic) {
      const { heicTo } = await import('heic-to/csp')
      source = await heicTo({ blob: file, type: 'bitmap' })
    } else {
      const image = new Image()
      objectUrl = URL.createObjectURL(file)
      image.src = objectUrl
      await image.decode()
      source = image
    }
    const width = source instanceof HTMLImageElement ? source.naturalWidth : source.width
    const height = source instanceof HTMLImageElement ? source.naturalHeight : source.height
    if (!width || !height || width * height > 64_000_000 || Math.max(width, height) > 16384) throw new Error('Nieprawidłowe wymiary obrazu.')
    const scale = Math.min(1, MAX_SIDE / width, MAX_SIDE / height)
    canvas.width = Math.max(1, Math.round(width * scale))
    canvas.height = Math.max(1, Math.round(height * scale))
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('Nie można przygotować obrazu.')
    ctx.imageSmoothingEnabled = true
    ctx.imageSmoothingQuality = 'high'
    ctx.drawImage(source, 0, 0, canvas.width, canvas.height)
    const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(
      (value) => value ? resolve(value) : reject(new Error('Nie można przygotować obrazu.')),
      'image/webp', 0.85,
    ))
    if (blob.size > MAX_UPLOAD_BYTES) throw new Error(ICON_TOO_LARGE)
    return new File([blob], `icon.${blob.type === 'image/webp' ? 'webp' : 'png'}`, { type: blob.type })
  } catch (error) {
    if (error instanceof Error && /^(Nieprawidłowe wymiary|Nie można przygotować|Wybrany plik jest za duży)/.test(error.message)) throw error
    throw new Error('Wybierz obsługiwany format pliku: JPEG, PNG, WebP, HEIC/HEIF.')
  } finally {
    if (source && 'close' in source) source.close()
    if (objectUrl) URL.revokeObjectURL(objectUrl)
    canvas.width = canvas.height = 0
  }
}
