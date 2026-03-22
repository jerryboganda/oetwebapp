import React from "react";
import { Card, CardBody, Row, Col, Button } from "reactstrap";
import { IconPhotoHeart, IconUser } from "@tabler/icons-react";

const ProfileCard = () => {
  return (
    <Card>
      <CardBody>
        <Row className="profile-container">
          <div className="image-details">
            <div className="profile-image mb-2"></div>
            <div className="profile-pic">
              <div className="avatar-upload position-relative">
                <div className="avatar-edit position-absolute top-0 end-0">
                  <input
                    type="file"
                    id="imageUpload"
                    accept=".png, .jpg, .jpeg"
                    hidden
                  />
                  <label htmlFor="imageUpload" className="cursor-pointer">
                    <IconPhotoHeart size={16} />
                  </label>
                </div>
                <div className="avatar-preview mt-3">
                  <div id="imgPreview"></div>
                </div>
              </div>
            </div>
          </div>
          <div className="person-details">
            <h5 className="f-w-600">
              Ninfa Monaldo
              <img
                width={20}
                height={20}
                src="/images/profile/01.png"
                alt="instagram-check-mark"
              />
            </h5>
            <p>Web designer &amp; Developer</p>

            <div className="details">
              <Col>
                <h4 className="text-primary">10</h4>
                <p className="text-secondary">Post</p>
              </Col>
              <Col>
                <h4 className="text-primary">3.4k</h4>
                <p className="text-secondary">Follower</p>
              </Col>
              <Col>
                <h4 className="text-primary">1k</h4>
                <p className="text-secondary">Following</p>
              </Col>
            </div>

            <div className="my-2">
              <Button color="primary" className="b-r-22">
                <IconUser size={16} className="me-1" />
                Follow
              </Button>
            </div>
          </div>
        </Row>
      </CardBody>
    </Card>
  );
};

export default ProfileCard;
