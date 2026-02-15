'use client'

import { useEffect, useState } from 'react'
import { useParams } from 'next/navigation'

type Person = {
  id: number
  name: string
  email: string
}

export default function SearchPage() {
  const { term } = useParams<{ term: string }>()
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<Person[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!term) return
    const decoded = decodeURIComponent(term)
    setQuery(decoded)
    search(decoded)
  }, [term])

  async function search(q: string) {
    try {
      setLoading(true)
      const res = await fetch(`/api/search?term=${encodeURIComponent(q)}`)
      const data = await res.json()
      setResults(data)
    } catch (err) {
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{ padding: 20 }}>
      <h2>Search: {query}</h2>

      {loading && <p>Loading...</p>}

      <ul>
        {results.map(person => (
          <li key={person.id}>
            <strong>{person.name}</strong> – {person.email}
          </li>
        ))}
      </ul>
    </div>
  )
}
