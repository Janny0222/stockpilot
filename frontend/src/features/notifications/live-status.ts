import { create } from 'zustand'

/** Whether the live channel is up. While it is, the bell stops polling; while it isn't, polling is the fallback. */
export const useLiveStatus = create<{ connected: boolean }>(() => ({ connected: false }))
