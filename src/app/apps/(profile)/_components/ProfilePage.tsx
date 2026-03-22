"use client";
import React, { useState } from "react";
import ProfileAppTabs from "./profileAppTabs";
import FriendsCard from "./FriendsCard";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import FeaturedPost from "./FeaturedPost";
import ProfileCard from "@/app/apps/(profile)/_components/ProfileCard";
import AboutMe from "@/app/apps/(profile)/_components/AboutMe";
import FeaturedStories from "@/app/apps/(profile)/_components/FeaturedStories";
import { Container, Row, Col } from "reactstrap";
import { IconStack2 } from "@tabler/icons-react";

const Profile = () => {
  const [data, setData] = useState<string>("1");
  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Profile"
        title="Apps"
        path={["Profile Page", "Profile"]}
        Icon={IconStack2}
      />
      <Row>
        <Col lg="3">
          <ProfileAppTabs data={data} setData={setData} />
          <FriendsCard />
          <FeaturedPost />
        </Col>

        <FeaturedStories data={data} />

        <Col lg="4" xxl="3" className="order--1-lg col-box-4">
          <ProfileCard />
          <AboutMe />
        </Col>
      </Row>
    </Container>
  );
};

export default Profile;
