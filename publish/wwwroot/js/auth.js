window.authStorage = {
    setUser: function (user) {
        localStorage.setItem("yk_user", JSON.stringify(user));
    },
    getUser: function () {
        return localStorage.getItem("yk_user");
    },
    clearUser: function () {
        localStorage.removeItem("yk_user");
    }
};

window.redirectTo = function (url) {
    window.location.replace(url);
};