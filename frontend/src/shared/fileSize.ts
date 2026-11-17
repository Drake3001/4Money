export const fileSizeMiB = (bytes: number) => (bytes / 1024 / 1024).toLocaleString('pl-PL', { maximumFractionDigits: 2 })

export const fileTooLarge = (bytes: number, subject: string) =>
  `Wybrany plik jest za duży. Maksymalny rozmiar pliku dla ${subject} to ${fileSizeMiB(bytes)} MiB.`
