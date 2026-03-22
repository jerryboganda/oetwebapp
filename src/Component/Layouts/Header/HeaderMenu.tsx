import React from "react";
import HeaderMode from "@/Component/Layouts/Header/HeaderMode";
import HeaderProfile from "@/Component/Layouts/Header/HeaderProfile";
import HeaderNotification from "@/Component/Layouts/Header/HeaderNotification";
import HeaderCart from "@/Component/Layouts/Header/HeaderCart";
import HeaderApps from "@/Component/Layouts/Header/HeaderApps";
import HeaderSearchbar from "@/Component/Layouts/Header/HeaderSearchbar";
import HeaderLanguage from "@/Component/Layouts/Header/HeaderLanguage";
import HeaderCloud from "@/Component/Layouts/Header/HeaderCloud";

const HeaderMenu = () => {
  return (
    <>
      <ul className="d-flex align-items-center">
        <li className="header-cloud">
          <HeaderCloud />
        </li>

        <li className="header-language">
          <HeaderLanguage />
        </li>

        <li className="header-searchbar">
          <HeaderSearchbar />
        </li>

        <li className="header-apps">
          <HeaderApps />
        </li>

        <li className="header-cart">
          <HeaderCart />
        </li>

        <li className="header-dark">
          <HeaderMode />
        </li>

        <li className="header-notification">
          <HeaderNotification />
        </li>

        <li className="header-profile">
          <HeaderProfile />
        </li>
      </ul>
    </>
  );
};

export default HeaderMenu;
