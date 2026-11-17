// Response/request DTOs exactly as documented in the 4Money API catalogue (rev. 95aa34b). camelCase JSON.
// Banking and Expense money values are decimal strings; Grouping money values are JSON numbers.

// ---- User
export type AuthenticationResponse = { accessToken: string; accessTokenExpiresAt: string; refreshToken: string; refreshTokenExpiresAt: string; tokenType: string }
export type UserProfileResponse = {
  id: string; login: string; name: string; surname: string; email: string; phone: string | null; birthDate: string; icon: string | null
  isEmailConfirmed: boolean; createdAt: string; lastLoginTime: string | null
  iconStorageKey: string | null; hasPassword: boolean; isGoogleLinked: boolean
}
export type RegisterUserResponse = { id: string; login: string; name: string; surname: string; email: string; birthDate: string; isEmailConfirmed: boolean; createdAt: string }
export type GoogleChallengeResponse = { clientId: string; nonce: string; expiresAt: string }
export type GoogleRegistrationInput = { idToken: string; login: string; name: string; surname: string; birthDate: string }
export type GoogleRegistrationResponse = { user: RegisterUserResponse; authentication: AuthenticationResponse | null; requiresEmailConfirmation: boolean }

// ---- Banking
export type StartBankConnectionResponse = { attemptId: string; authorizationUrl: string; expiresAt: string }
export type BankConnectionAttemptResponse = { id: string; status: string; connectionId: string | null; createdAt: string; expiresAt: string; completedAt: string | null; failureCode: string | null }
export type BankConnectionResponse = {
  id: string; providerName: string | null; connectionStatus: string; syncStatus: string; createdAt: string; connectedAt: string | null
  consentExpiresAt: string | null; lastSuccessfulSyncAt: string | null; lastSyncFailureCode: string | null
}
export type BankConsentResponse = { connectionId: string; providerStatus: string; sessionExpiresAt: string | null; sessionExtendable: boolean | null }
export type StartBankRefreshResponse = { refreshId: string; status: string; createdAt: string }
export type BankRefreshAccountResponse = { accountId: string; status: string; addedTransactions: number; updatedTransactions: number; lastSuccessfulSyncAt: string | null; failureCode: string | null }
export type BankRefreshResponse = {
  refreshId: string; status: string; createdAt: string; startedAt: string | null; completedAt: string | null
  totalAccounts: number; successfulAccounts: number; failedAccounts: number; failureCode: string | null; accounts: BankRefreshAccountResponse[]
}
export type BankAccountResponse = {
  id: string; name: string; number: string | null; iban: string | null; financialInstitutionId: string | null; type: string | null
  currencyCode: string; isClosed: boolean; lastSuccessfulSyncAt: string | null; transactionsSyncedThrough: string | null
}
export type BankTransactionResponse = { id: string; bankAccountId: string; amount: string; currencyCode: string; date: string; description: string | null; status: string }

// ---- Grouping
export type GroupResponse = {
  id: string; name: string; icon: string | null; adminUserId: string; currencyCode: string; isMember: boolean
  description: string | null; iconStorageKey: string | null; isArchived: boolean
}
export type GroupMemberResponse = {
  userId: string; groupId: string; isMember: boolean; joinedAt: string; totalPaid: number; totalShare: number
  totalSettlementsSent: number; totalSettlementsReceived: number
  displayName?: string | null
}
export type GroupInvitationResponse = { id: string; groupId: string; createdByUserId: string; maxUses: number; useCount: number; expiresAt: string; isActive: boolean }
export type CreateGroupInvitationResponse = { invitation: GroupInvitationResponse; token: string }
export type InvitationPreview = { groupId: string; name: string; description: string | null; currencyCode: string; icon: string | null; image: FileUrlResult | null; expiresAt: string; remainingUses: number }
export type GroupIconPreset = { key: string; url: string; expiresAt: string }
export type FileUrlResult = { url: string; expiresAt: string }
export type GroupSettlementResponse = { id: string; groupId: string; fromUserId: string; toUserId: string; amount: number; method: number; settledAt: string; bankTransactionId: string | null }
export type GroupSettlementRequest = { fromUserId: string; toUserId: string; amount: number; method: number; settledAt: string; bankTransactionId?: null }
export type GroupMemberBalanceResponse = { userId: string; isMember: boolean; balance: number }
export type SuggestedSettlementResponse = { fromUserId: string; toUserId: string; amount: number }
export type GroupSettlementSuggestionsResponse = {
  groupId: string; currencyCode: string; generatedAt: string; algorithm: string; optimalityGuaranteed: boolean
  balances: GroupMemberBalanceResponse[]; suggestions: SuggestedSettlementResponse[]; totalAmount: number
}

// ---- Expense
export type CategoryResponse = { id: string; name: string; description: string; icon: string; isSystemGenerated: boolean }
export type AvailableBankTransactionResponse = BankTransactionResponse
export type ExpenseShareResponse = { userId: string; userDisplayName: string; percentage: string; amount: string }
export type ExpenseItemResponse = {
  id: string; categoryId: string; categoryName: string; categoryIcon: string; name: string; unitPrice: string; quantity: string
  discount: string; unit: string | null; total: string; isSystemGenerated: boolean
}
export type PreviewItemResponse = Omit<ExpenseItemResponse, 'id'>
export type ExpenseResponse = {
  id: string; groupId: string; groupName: string; payerId: string; payerDisplayName: string; name: string; merchant: string | null
  amount: string; currencyCode: string; transactionTime: string; bankTransactionId: string | null; receiptId: string | null
  items: ExpenseItemResponse[]; shares: ExpenseShareResponse[]; version: string; canEdit: boolean; canDelete: boolean
}
export type ExpensePreviewResponse = { amount: string; currencyCode: string; items: PreviewItemResponse[]; shares: ExpenseShareResponse[]; bankTransactionId: string | null; receiptId: string | null }
export type ExpenseListItemResponse = {
  id: string; groupId: string; groupName: string; payerId: string; payerDisplayName: string; name: string; merchant: string | null; amount: string
  currencyCode: string; transactionTime: string; bankTransactionId: string | null; receiptId: string | null; myShareAmount: string; mySharePercentage: string
  categories: CategoryResponse[]; hasReceipt: boolean; hasBankTransaction: boolean
}
export type ReceiptDraftItem = { categoryId: string | null; name: string | null; unitPrice: string | null; quantity: string | null; discount: string | null; unit: string | null; needsReview: boolean; reviewReason: string | null }
export type ReceiptDraft = { name: string | null; merchant: string | null; amount: string | null; currencyCode: string | null; transactionTime: string | null; items: ReceiptDraftItem[] }
export type ReceiptWarning = { code: string | null; message: string; field: string | null }
export type ReceiptAnalysisResponse = { proposal: ReceiptDraft; warnings: ReceiptWarning[]; needsReview: boolean }
export type ReceiptOcrResponse = ReceiptAnalysisResponse & { ocrProof: string }
export type ReceiptResponse = {
  id: string; expenseId: string; fileUrl: string; fileUrlExpiresAt: string
  analysis: ReceiptAnalysisResponse | null
}

// ---- Notifications
export type NotificationResponse = { id: string; title: string; text: string; type: number; createdAt: string; groupId: string | null; expenseId: string | null; readAt: string | null; isRead: boolean }
