/**
 * @fileoverview Базовые утилиты для Tiny City Online
 * @description Общие вспомогательные функции для всего приложения
 */

'use strict';

/**
 * Пространство имен для общих утилит приложения
 */
const AppUtils = {
    /**
     * Генерирует случайное число в диапазоне
     * @param {number} min - Минимальное значение
     * @param {number} max - Максимальное значение
     * @returns {number} Случайное число
     */
    randomRange(min, max) {
        return Math.floor(Math.random() * (max - min + 1)) + min;
    },

    /**
     * Сохраняет данные в localStorage с обработкой ошибок
     * @param {string} key - Ключ для хранения
     * @param {any} value - Значение для сохранения
     * @returns {boolean} Успешность операции
     */
    saveToStorage(key, value) {
        try {
            localStorage.setItem(key, JSON.stringify(value));
            return true;
        } catch (error) {
            console.error('[AppUtils] Ошибка сохранения в localStorage:', error);
            return false;
        }
    },

    /**
     * Получает данные из localStorage с обработкой ошибок
     * @param {string} key - Ключ для получения
     * @param {any} defaultValue - Значение по умолчанию
     * @returns {any} Полученное значение или default
     */
    getFromStorage(key, defaultValue = null) {
        try {
            const item = localStorage.getItem(key);
            return item ? JSON.parse(item) : defaultValue;
        } catch (error) {
            console.error('[AppUtils] Ошибка чтения из localStorage:', error);
            return defaultValue;
        }
    },

    /**
     * Форматирует число с разделителями тысяч
     * @param {number} num - Число для форматирования
     * @returns {string} Отформатированная строка
     */
    formatNumber(num) {
        return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
    },

    /**
     * Задерживает выполнение на указанное время
     * @param {number} ms - Время задержки в миллисекундах
     * @returns {Promise} Промис для await
     */
    delay(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    },

    /**
     * Проверяет, является ли значение пустым
     * @param {any} value - Значение для проверки
     * @returns {boolean} True если значение пустое
     */
    isEmpty(value) {
        return value === null || value === undefined || value === '';
    }
};

/**
 * Инициализация при загрузке страницы
 */
document.addEventListener('DOMContentLoaded', () => {
    console.log('[Site.js] Приложение инициализировано');
});