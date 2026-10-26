export type SourceTier = 'Official' | 'Media' | 'Reference' | 'Unofficial'

export type PipelineStage =
  | 'Planning'
  | 'Searching'
  | 'Filtering'
  | 'Downloading'
  | 'ExtractingClaims'
  | 'Verifying'
  | 'Answering'

export interface PipelineProgress {
  stage: PipelineStage
  message: string
}

export interface SourceDto {
  number: number
  title: string
  url: string
  domain: string
  tier: SourceTier
  publishedAt: string | null
}

export interface ClaimDto {
  text: string
  confidencePercent: number
  supporting: number[]
  contradicting: number[]
}

export interface AnswerDto {
  question: string
  summary: string
  overallConfidencePercent: number
  claims: ClaimDto[]
  sources: SourceDto[]
  language: string
  needsFreshness: boolean
  queries: string[]
  warnings: string[]
  fromCache: boolean
}