/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
      "./index.html",
      "./**/*.{fs,js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {},
  },
  plugins: [require("daisyui")]
}

