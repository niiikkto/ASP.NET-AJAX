```c#
// Создаём контроллер. У него есть метод abort() и свойство signal.
//Состояние   Что означает
//idle        Запрос ещё не отправлялся
//loading     Запрос в процессе
//success     Запрос завершён успешно, есть данные
//error       Запрос завершился ошибкой
//aborted     Запрос отменён вручную (частный случай error, но отличается тем, что это не «настоящая» ошибка)

using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Mime;
using System.Threading;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;
// AbortbortController + fetch
const controller = new AbortController();

// Передаём signal в fetch. Теперь запрос можно отменить.
fetch('/api/users', { signal: controller.signal })
  .then((response) => {
   // ВАЖНО: fetch не бросает ошибку на 404/500 — нужно проверить вручную.
   if (!response.ok)
   {
       throw new Error(HTTP ${ response.status }: ${ response.statusText});
    }
    return response.json();
  })
  .then((data) => {
       console.log('Данные:', data);
   })
  .catch((error) => {
      // Отличаем отмену от настоящей ошибки.
      if (error.name === 'AbortError')
      {
          console.log('Запрос отменён — это не ошибка, просто игнорируем.');
          return;
      }
      console.error('Настоящая ошибка:', error);
  });

// Через 300 мс отменяем запрос (например, пользователь ввёл новый символ).
setTimeout(() => controller.abort(), 300);

//Отмена по тайм-ауту
// AbortSignal.timeout(ms) — удобный способ отменить запрос по истечении времени.
// Не требует ручного создания контроллера.
async function fetchWithTimeout(url, ms = 5000)
{
    try
    {
        const response = await fetch(url, {
        signal: AbortSignal.timeout(ms),
    });
        if (!response.ok) throw new Error(HTTP ${ response.status });
        return await response.json();
    }
    catch (error)
    {
        // signal.timeout бросает DOMException с именем 'TimeoutError'
        if (error.name === 'TimeoutError')
        {
            throw new Error(Запрос превысил лимит ${ ms } мс);
        }
        throw error;
    }
}

// Объединение сигналов
// AbortSignal.any([...]) объединяет несколько сигналов в один.
// Сработает первый из них.
function requestWithCancelAndTimeout(url, userSignal, timeoutMs = 8000)
{
    const timeoutSignal = AbortSignal.timeout(timeoutMs);
    const combinedSignal = AbortSignal.any([userSignal, timeoutSignal]);

    return fetch(url, { signal: combinedSignal });
}

// Использование:
const controller = new AbortController();
requestWithCancelAndTimeout('/api/data', controller.signal, 5000)
  .then((r) => r.json())
  .then(console.log)
  .catch((e) => {
      if (e.name === 'AbortError') return; // отмена пользователем
      if (e.name === 'TimeoutError') console.warn('Таймаут');
  });

// Позже:
controller.abort();


// Гонка http-запросов
// ❌ ПЛОХО: классическая гонка без защиты.
// Представим, что это обработчик ввода в поиске.
let currentQuery = '';

async function searchBad(query)
{
    currentQuery = query;
    const response = await fetch(/ api / search ? q =${ encodeURIComponent(query)});
    const data = await response.json();

    // Если пользователь уже ввёл новое значение — этот ответ устарел,
    // но мы всё равно его рендерим. Это баг.
    renderResults(data);
}

// Пользователь печатает быстро:
searchBad('a');    // запрос 1 — вернётся через 800 мс
searchBad('ab');   // запрос 2 — вернётся через 200 мс
searchBad('abc');  // запрос 3 — вернётся через 400 мс
// Итог: отрендерятся данные для 'a', хотя в поле 'abc'.


// ✅ ХОРОШО: отменяем предыдущий запрос перед новым.
let activeController = null;

async function searchWithAbort(query)
{
    // Если есть активный запрос — отменяем его.
    if (activeController)
    {
        activeController.abort();
    }
    activeController = new AbortController();

    try
    {
        const response = await fetch(
    
          / api / search ? q =${ encodeURIComponent(query)},
      { signal: activeController.signal }
    );
        if (!response.ok) throw new Error(HTTP ${ response.status });
        const data = await response.json();

        // Дополнительная проверка: не отменён ли запрос, пока мы парсили JSON.
        if (activeController.signal.aborted) return;

        renderResults(data);
    }
    catch (error)
    {
        // Отмена — это норма, не показываем пользователю.
        if (error.name === 'AbortError') return;
        renderError(error);
    }
}

// ✅ ХОРОШО: даже если отмена не сработала (например, ответ уже в пути),
// мы игнорируем устаревшие ответы по номеру.
let requestId = 0;

async function searchWithSeq(query) {
  const myId = ++requestId;

  try {
    const response = await fetch(/api/search?q=${encodeURIComponent(query)});
    const data = await response.json();

    // Если за время запроса был запущен более новый — игнорируем этот.
    if (myId !== requestId) return;

    renderResults(data);
  } catch (error) {
    if (myId !== requestId) return; // устаревшая ошибка — тоже игнорируем
    renderError(error);
  }
}
// ✅ ЛУЧШЕ ВСЕГО: отмена + счётчик. Отмена экономит ресурсы,
// счётчик подстраховывает от гонок при уже начавшемся парсинге.
let currentController = null;
let currentRequestId = 0;

async function search(query, { onLoading, onSuccess, onError } = {}) {
  // Отменяем предыдущий запрос, если он ещё в процессе.
  if (currentController) currentController.abort();
  currentController = new AbortController();

  const myId = ++currentRequestId;
  const signal = currentController.signal;

  onLoading?.(true);

  try {
    const response = await fetch(
      /api/search?q=${encodeURIComponent(query)},
      { signal }
    );

    // Проверяем HTTP-статус вручную.
    if (!response.ok) {
      throw new Error(Сервер вернул ${response.status});
    }

    const data = await response.json();

    // Игнорируем, если это устаревший запрос (пока парсили JSON, пришёл новый).
    if (myId !== currentRequestId) return;

    onSuccess?.(data);
  } catch (error) {
    // Игнорируем устаревшие ошибки.
    if (myId !== currentRequestId) return;

    // Отмена — не ошибка.
    if (error.name === 'AbortError') return;

    onError?.(error);
  } finally {
    // Снимаем loading только если это всё ещё актуальный запрос.
    if (myId === currentRequestId) onLoading?.(false);
  }
}
import { useEffect, useReducer, useRef } from 'react';

// Единый reducer — не даёт противоречивых состояний
// (например, loading=true и error!=null одновременно).
function reducer(state, action) {
  switch (action.type) {
    case 'start':
      return { status: 'loading', data: null, error: null };
    case 'success':
      return { status: 'success', data: action.payload, error: null };
    case 'error':
      return { status: 'error', data: null, error: action.payload };
    default:
      return state;
  }
}

export function useFetch(url, options = {}) {
  const [state, dispatch] = useReducer(reducer, {
    status: 'idle',
    data: null,
    error: null,
  });

  // useRef, чтобы не пересоздавать контроллер при ре-рендере.
  const controllerRef = useRef(null);

  useEffect(() => {
    if (!url) return;

    // Отменяем предыдущий запрос (если URL изменился быстро).
    if (controllerRef.current) controllerRef.current.abort();
    const controller = new AbortController();
    controllerRef.current = controller;

    dispatch({ type: 'start' });

    fetch(url, { ...options, signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(HTTP ${response.status});
        return response.json();
      })
      .then((data) => {
        // Если запрос отменён — не трогаем состояние.
        if (controller.signal.aborted) return;
        dispatch({ type: 'success', payload: data });
      })
      .catch((error) => {
        // Отмена — не ошибка, ничего не делаем.
        if (error.name === 'AbortError') return;
        dispatch({ type: 'error', payload: error });
      });

    // Cleanup: вызывается при размонтировании или смене url.
    // Именно он спасает от гонок и утечек памяти.
    return () => controller.abort();
  }, [url]);

  return state;
}// ✅ ХОРОШО: даже если отмена не сработала (например, ответ уже в пути),
// мы игнорируем устаревшие ответы по номеру.
let requestId = 0;

async function searchWithSeq(query) {
  const myId = ++requestId;

  try {
    const response = await fetch(/api/search?q=${encodeURIComponent(query)});
    const data = await response.json();

    // Если за время запроса был запущен более новый — игнорируем этот.
    if (myId !== requestId) return;

    renderResults(data);
  } catch (error) {
    if (myId !== requestId) return; // устаревшая ошибка — тоже игнорируем
    renderError(error);
  }
}
// ✅ ЛУЧШЕ ВСЕГО: отмена + счётчик. Отмена экономит ресурсы,
// счётчик подстраховывает от гонок при уже начавшемся парсинге.
let currentController = null;
let currentRequestId = 0;

async function search(query, { onLoading, onSuccess, onError } = {}) {
  // Отменяем предыдущий запрос, если он ещё в процессе.
  if (currentController) currentController.abort();
  currentController = new AbortController();

  const myId = ++currentRequestId;
  const signal = currentController.signal;

  onLoading?.(true);

  try {
    const response = await fetch(
      /api/search?q=${encodeURIComponent(query)},
      { signal }
    );

    // Проверяем HTTP-статус вручную.
    if (!response.ok) {
      throw new Error(Сервер вернул ${response.status});
    }

    const data = await response.json();

    // Игнорируем, если это устаревший запрос (пока парсили JSON, пришёл новый).
    if (myId !== currentRequestId) return;

    onSuccess?.(data);
  } catch (error) {
    // Игнорируем устаревшие ошибки.
    if (myId !== currentRequestId) return;

    // Отмена — не ошибка.
    if (error.name === 'AbortError') return;

    onError?.(error);
  } finally {
    // Снимаем loading только если это всё ещё актуальный запрос.
    if (myId === currentRequestId) onLoading?.(false);
  }
}

//use fetch с полным состоянием
import { useEffect, useReducer, useRef } from 'react';

// Единый reducer — не даёт противоречивых состояний
// (например, loading=true и error!=null одновременно).
function reducer(state, action) {
  switch (action.type) {
    case 'start':
      return { status: 'loading', data: null, error: null };
    case 'success':
      return { status: 'success', data: action.payload, error: null };
    case 'error':
      return { status: 'error', data: null, error: action.payload };
    default:
      return state;
  }
}

export function useFetch(url, options = {}) {
  const [state, dispatch] = useReducer(reducer, {
    status: 'idle',
    data: null,
    error: null,
  });

  // useRef, чтобы не пересоздавать контроллер при ре-рендере.
  const controllerRef = useRef(null);

  useEffect(() => {
    if (!url) return;

    // Отменяем предыдущий запрос (если URL изменился быстро).
    if (controllerRef.current) controllerRef.current.abort();
    const controller = new AbortController();
    controllerRef.current = controller;

    dispatch({ type: 'start' });

    fetch(url, { ...options, signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(HTTP ${response.status});
        return response.json();
      })
      .then((data) => {
        // Если запрос отменён — не трогаем состояние.
        if (controller.signal.aborted) return;
        dispatch({ type: 'success', payload: data });
      })
      .catch((error) => {
        // Отмена — не ошибка, ничего не делаем.
        if (error.name === 'AbortError') return;
        dispatch({ type: 'error', payload: error });
      });

    // Cleanup: вызывается при размонтировании или смене url.
    // Именно он спасает от гонок и утечек памяти.
    return () => controller.abort();
  }, [url]);

  return state;
}


// компоненты с ui ля загрузки/ошибки/успеха
import { useFetch } from './useFetch';

export function UserProfile({ userId }) {
    const { status, data, error } = useFetch(/ api / users /${ userId});

    if (status === 'loading' || status === 'idle')
    {
        return < div className = "spinner" > Загрузка…</ div >;
    }

    if (status === 'error')
    {
        return (
    
          < div className = "error" >
    
            < p > Ошибка: { error.message}</ p >
    
            < button onClick ={ () => window.location.reload()}> Повторить </ button >
    
          </ div >
      );
    }

    return (
  
      < div >
  
        < h1 >{ data.name}</ h1 >
  
        < p >{ data.email}</ p >
  
      </ div >
  );
}


// Debounce откладывает выполнение, пока пользователь не перестанет печатать.
// AbortController отменяет предыдущий запрос, если пользователь всё-таки продолжил.
function debounce(fn, delay)
{
    let timerId = null;
    return function(...args) {
        clearTimeout(timerId);
        timerId = setTimeout(() => fn.apply(this, args), delay);
    }
    ;
}

let searchController = null;

const runSearch = async (query) => {
    if (searchController) searchController.abort();
    searchController = new AbortController();

    try
    {
        const res = await fetch(
    
          / api / search ? q =${ encodeURIComponent(query)},
      { signal: searchController.signal }
    );
        const data = await res.json();
        renderResults(data);
    }
    catch (e)
    {
        if (e.name === 'AbortError') return;
        renderError(e);
    }
};

const debouncedSearch = debounce(runSearch, 300);

document.querySelector('#search').addEventListener('input', (e) => {
    debouncedSearch(e.target.value);
});




// Гонка может возникнуть и при двойном клике по «Отправить».
// Решение: блокируем кнопку + отменяем предыдущий запрос.
let submitController = null;
let isSubmitting = false;

async function handleSubmit(formData, button)
{
    if (isSubmitting) return; // защита от повторного клика

    if (submitController) submitController.abort();
    submitController = new AbortController();

    isSubmitting = true;
    button.disabled = true;
    button.textContent = 'Отправка…';

    try
    {
        const res = await fetch('/api/submit', {
        method: 'POST',
      body: formData,
      signal: submitController.signal,
    });
        if (!res.ok) throw new Error(HTTP ${ res.status });
        showSuccess();
    }
    catch (e)
    {
        if (e.name === 'AbortError') return;
        showError(e);
    }
    finally
    {
        isSubmitting = false;
        button.disabled = false;
        button.textContent = 'Отправить';
    }
}




/**
 * Обёртка над fetch, которая:
 *  - бросает настоящие ошибки для HTTP 4xx/5xx,
 *  - не бросает для AbortError (возвращает { aborted: true }),
 *  - поддерживает таймаут.
 */
export async function safeFetch(url, { timeout = 10000, signal, ...init } = { }) {
    const timeoutSignal = AbortSignal.timeout(timeout);
    const combined = signal
      ? AbortSignal.any([signal, timeoutSignal])
      : timeoutSignal;

    try
    {
        const response = await fetch(url, { ...init, signal: combined });

        if (!response.ok)
        {
            // Пытаемся достать сообщение из тела ответа, если оно там есть.
            let detail = '';
            try
            {
                const body = await response.json();
                detail = body?.message ?? '';
            }
            catch
            {
                /* тело не JSON — игнорируем */
            }
            throw new Error(
              HTTP ${ response.status } ${ response.statusText}${ detail ? ': ' + detail : ''}
      );
        }

        // Парсим в зависимости от Content-Type.
        const contentType = response.headers.get('content-type') ?? '';
        if (contentType.includes('application/json')) return await response.json();
        return await response.text();
    }
    catch (error)
    {
        if (error.name === 'AbortError')
        {
            // Явно различаем отмену пользователем и таймаут.
            if (timeoutSignal.aborted && !signal?.aborted)
            {
                throw new Error('Превышено время ожидания ответа');
            }
            // Отмена пользователем — возвращаем маркер, не бросаем.
            return { aborted: true }
            ;
        }
        // Сетевые ошибки (нет интернета, DNS и т.п.) прилетают как TypeError.
        if (error instanceof TypeError) {
            throw new Error('Сетевая ошибка: проверьте подключение');
        }
        throw error;
    }
}

// Патерн последней запрос побеждает
// Универсальный хелпер: создаёт функцию-обёртку, которая гарантирует,
// что обработан будет только результат последнего вызова.
function createLatestOnly()
{
    let controller = null;
    let id = 0;

    return async function latestOnly(fn)
    {
        if (controller) controller.abort();
        controller = new AbortController();
        const myId = ++id;

        try
        {
            const result = await fn(controller.signal);
            // Если за это время был вызван новый — игнорируем.
            if (myId !== id) return undefined;
            return result;
        }
        catch (e)
        {
            if (myId !== id) return undefined;
            throw e;
        }
    }
    ;
}

// Использование:
const latest = createLatestOnly();


async function loadUser(id)
{
    return latest(async (signal) => {
    const res = await fetch(/ api / users /${ id}, { signal });
    if (!res.ok) throw new Error(HTTP ${ res.status });
    return res.json();
});
}

// Быстрые клики по разным пользователям — отрендерится только последний.
loadUser(1);
loadUser(2);
loadUser(3); // победит этот


```