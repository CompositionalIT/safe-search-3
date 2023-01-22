export default ({
    // proxy API calls to the backend
    server: {
        proxy: {
            '/api': 'http://localhost:5000',
        }
    },
    // needed for fable watch
    define: {
        global: {}
    }
})